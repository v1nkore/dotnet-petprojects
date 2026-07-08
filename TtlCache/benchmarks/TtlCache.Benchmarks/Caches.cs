using System.Collections.Concurrent;

namespace TtlCache.Benchmarks;

/// <summary>
/// Минимальный контракт «словарь с синхронизацией» — без TTL,
/// чтобы бенчмарк сравнивал только стратегии синхронизации, а не логику кэша.
/// </summary>
public interface IBenchCache
{
    bool TryGet(int key, out int value);
    void Set(int key, int value);
}

/// <summary>
/// Обычный Dictionary + lock. Простейшая и часто достаточная стратегия:
/// один писатель ИЛИ один читатель в каждый момент времени.
/// System.Threading.Lock (.NET 9) вместо lock(object): выделенный тип,
/// компилятор генерирует EnterScope вместо Monitor.Enter — чуть быстрее
/// и нельзя по ошибке залочиться на чём попало.
/// </summary>
public sealed class LockDictionaryCache : IBenchCache
{
    private readonly Dictionary<int, int> _map = [];
    private readonly Lock _gate = new();

    public bool TryGet(int key, out int value)
    {
        lock (_gate)
            return _map.TryGetValue(key, out value);
    }

    public void Set(int key, int value)
    {
        lock (_gate)
            _map[key] = value;
    }
}

/// <summary>
/// ConcurrentDictionary: лок-страйпинг на запись, чтение вообще без блокировок
/// (volatile-чтение бакета). Под конкурентной read-heavy нагрузкой ожидаемо лидирует.
/// </summary>
public sealed class ConcurrentDictionaryCache : IBenchCache
{
    private readonly ConcurrentDictionary<int, int> _map = new();

    public bool TryGet(int key, out int value) => _map.TryGetValue(key, out value);

    public void Set(int key, int value) => _map[key] = value;
}

/// <summary>
/// ReaderWriterLockSlim: много параллельных читателей, писатель — эксклюзивно.
/// Звучит идеально для кэша, но вход/выход даже в read-lock дороже, чем
/// Monitor: на коротких операциях (доступ к словарю ~20 нс) накладные расходы
/// съедают весь выигрыш от параллельного чтения. Классическая ловушка.
/// </summary>
public sealed class ReaderWriterLockSlimCache : IBenchCache
{
    private readonly Dictionary<int, int> _map = [];
    private readonly ReaderWriterLockSlim _rwLock = new();

    public bool TryGet(int key, out int value)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _map.TryGetValue(key, out value);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void Set(int key, int value)
    {
        _rwLock.EnterWriteLock();
        try
        {
            _map[key] = value;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
}
