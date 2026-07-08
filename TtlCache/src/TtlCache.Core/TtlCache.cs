using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace TtlCache.Core;

/// <summary>
/// Потокобезопасный in-memory кэш с TTL на каждую запись.
/// Протухшие записи удаляются двумя путями:
///   1) лениво — при чтении (TryGet видит протухшую запись и убирает её);
///   2) активно — фоновым свипером на PeriodicTimer, чтобы память не росла
///      от ключей, которые никто больше не читает.
/// </summary>
public sealed class TtlCache<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _entries = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _defaultTtl;
    private readonly CancellationTokenSource _cts = new();
    private readonly PeriodicTimer _cleanupTimer;
    private readonly Task _cleanupLoop;

    private long _hits;
    private long _misses;

    /// <param name="defaultTtl">TTL по умолчанию для записей без явного TTL.</param>
    /// <param name="cleanupInterval">Период фоновой очистки. По умолчанию — 1/10 от TTL, но не чаще раза в секунду.</param>
    /// <param name="timeProvider">Источник времени. В проде — системный, в тестах — FakeTimeProvider.</param>
    public TtlCache(TimeSpan defaultTtl, TimeSpan? cleanupInterval = null, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(defaultTtl, TimeSpan.Zero);

        _defaultTtl = defaultTtl;
        _time = timeProvider ?? TimeProvider.System;

        var interval = cleanupInterval
            ?? TimeSpan.FromTicks(Math.Max(defaultTtl.Ticks / 10, TimeSpan.TicksPerSecond));

        // Таймер создаётся синхронно в конструкторе, а не внутри фоновой задачи:
        // иначе гонка — FakeTimeProvider в тесте можно промотать раньше, чем таймер
        // успеет в нём зарегистрироваться, и тик никогда не наступит.
        _cleanupTimer = new PeriodicTimer(interval, _time);

        // Свипер живёт на пуле потоков, а не на выделенном потоке: работа редкая и короткая.
        _cleanupLoop = Task.Run(() => CleanupLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Запись кэша. readonly record struct: хранится по значению внутри словаря,
    /// без отдельного объекта в куче на каждую запись и без боксинга.
    /// Вместо «момента истечения» храним пару (когда положили, сколько жить) —
    /// GetTimestamp() монотонен и не зависит от перевода системных часов.
    /// </summary>
    private readonly record struct CacheEntry(TValue Value, long CreatedAt, TimeSpan Ttl);

    private bool IsExpired(in CacheEntry entry) => _time.GetElapsedTime(entry.CreatedAt) >= entry.Ttl;

    /// <summary>Количество записей, включая протухшие, которые ещё не убрал свипер.</summary>
    public int Count => _entries.Count;

    public CacheStats Stats => new(Interlocked.Read(ref _hits), Interlocked.Read(ref _misses));

    public void Set(TKey key, TValue value, TimeSpan? ttl = null)
    {
        ObjectDisposedException.ThrowIf(_cts.IsCancellationRequested, this);
        _entries[key] = new CacheEntry(value, _time.GetTimestamp(), ttl ?? _defaultTtl);
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (!IsExpired(entry))
            {
                Interlocked.Increment(ref _hits);
                value = entry.Value;
                return true;
            }

            // Ленивое удаление. TryRemove(KeyValuePair) атомарно удаляет пару
            // «ключ + именно это значение»: если другой поток успел перезаписать
            // ключ свежими данными, мы их не снесём (обычный TryRemove(key) снёс бы).
            _entries.TryRemove(KeyValuePair.Create(key, entry));
        }

        Interlocked.Increment(ref _misses);
        value = default;
        return false;
    }

    /// <summary>
    /// Вернуть из кэша или создать через фабрику.
    /// Нюанс: при одновременном промахе на одном ключе фабрика может выполниться
    /// в нескольких потоках, выигрывает последняя запись. Для дешёвых фабрик это
    /// норма (так же ведёт себя ConcurrentDictionary.GetOrAdd). Защита дорогих
    /// фабрик от stampede — через Lazy или SemaphoreSlim — отдельная тема (кэш-проект про Redis).
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory, TimeSpan? ttl = null)
    {
        if (TryGet(key, out var existing))
            return existing;

        var created = factory(key);
        Set(key, created, ttl);
        return created;
    }

    public bool Remove(TKey key) => _entries.TryRemove(key, out _);

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        // PeriodicTimer вместо System.Timers.Timer/Task.Delay-цикла:
        // не накладывает тики друг на друга, если очистка затянулась,
        // и умеет работать от внешнего TimeProvider (тестируемость).
        try
        {
            while (await _cleanupTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                RemoveExpired();
        }
        catch (OperationCanceledException)
        {
            // штатная остановка через Dispose
        }
    }

    /// <summary>Один проход свипера. Возвращает число удалённых записей (удобно в тестах).</summary>
    internal int RemoveExpired()
    {
        var removed = 0;

        // Перечисление ConcurrentDictionary lock-free и не бросает при
        // параллельных изменениях — снимок «примерно сейчас», этого достаточно.
        foreach (var pair in _entries)
        {
            if (IsExpired(pair.Value) && _entries.TryRemove(pair))
                removed++;
        }

        return removed;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cleanupTimer.Dispose();
        _cts.Dispose();
    }
}

/// <summary>Снимок счётчиков. Hit ratio — первый вопрос к любому кэшу в проде.</summary>
public readonly record struct CacheStats(long Hits, long Misses)
{
    public double HitRatio => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);
}
