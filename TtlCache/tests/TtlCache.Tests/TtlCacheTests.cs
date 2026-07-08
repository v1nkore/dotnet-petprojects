using Microsoft.Extensions.Time.Testing;
using TtlCache.Core;

namespace TtlCache.Tests;

/// <summary>
/// Ключевая идея тестов: время — это зависимость, и её надо инжектить.
/// FakeTimeProvider позволяет «проматывать часы» вручную — тесты на TTL
/// выполняются мгновенно и не флакуют, в отличие от Task.Delay + Thread.Sleep.
/// </summary>
public class TtlCacheTests
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    [Fact]
    public void TryGet_ReturnsValue_BeforeTtlExpires()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);

        cache.Set("answer", 42);
        time.Advance(TimeSpan.FromMinutes(4));

        Assert.True(cache.TryGet("answer", out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGet_ReturnsFalse_AfterTtlExpires()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);

        cache.Set("answer", 42);
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(cache.TryGet("answer", out _));
    }

    [Fact]
    public void PerEntryTtl_OverridesDefault()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);

        cache.Set("short-lived", 1, TimeSpan.FromSeconds(30));
        cache.Set("default", 2);

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.False(cache.TryGet("short-lived", out _));
        Assert.True(cache.TryGet("default", out _));
    }

    [Fact]
    public void TryGet_RemovesExpiredEntryLazily()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);

        cache.Set("stale", 1);
        time.Advance(TimeSpan.FromMinutes(10));

        cache.TryGet("stale", out _);

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Set_RefreshesTtl_ForExistingKey()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);

        cache.Set("key", 1);
        time.Advance(TimeSpan.FromMinutes(4));
        cache.Set("key", 2); // перезапись сбрасывает отсчёт TTL
        time.Advance(TimeSpan.FromMinutes(4));

        Assert.True(cache.TryGet("key", out var value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void RemoveExpired_SweepsOnlyExpiredEntries()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);

        cache.Set("old", 1);
        time.Advance(TimeSpan.FromMinutes(6));
        cache.Set("fresh", 2);

        var removed = cache.RemoveExpired();

        Assert.Equal(1, removed);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("fresh", out _));
    }

    [Fact]
    public async Task BackgroundCleanup_RemovesExpiredEntries_WithoutReads()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(
            TimeSpan.FromMinutes(1),
            cleanupInterval: TimeSpan.FromMinutes(2),
            timeProvider: time);

        cache.Set("abandoned", 1);

        // Advance двигает и TTL записи, и PeriodicTimer свипера.
        time.Advance(TimeSpan.FromMinutes(2));

        // Тик таймера будит свипер на пуле потоков — даём его continuation
        // выполниться. Опрос с таймаутом вместо слепого Task.Delay(500):
        // быстрый в счастливом пути и не флакует на загруженной машине.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (cache.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void GetOrAdd_InvokesFactoryOnlyOnMiss()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);
        var factoryCalls = 0;

        var first = cache.GetOrAdd("key", _ => { factoryCalls++; return 42; });
        var second = cache.GetOrAdd("key", _ => { factoryCalls++; return 99; });

        Assert.Equal(42, first);
        Assert.Equal(42, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void Stats_TracksHitsAndMisses()
    {
        var time = new FakeTimeProvider();
        using var cache = new TtlCache<string, int>(DefaultTtl, timeProvider: time);

        cache.Set("key", 1);
        cache.TryGet("key", out _);     // hit
        cache.TryGet("key", out _);     // hit
        cache.TryGet("missing", out _); // miss

        Assert.Equal(new CacheStats(Hits: 2, Misses: 1), cache.Stats);
        Assert.Equal(2.0 / 3.0, cache.Stats.HitRatio, precision: 5);
    }

    [Fact]
    public void Set_Throws_AfterDispose()
    {
        var cache = new TtlCache<string, int>(DefaultTtl);
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.Set("key", 1));
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotCorruptState()
    {
        using var cache = new TtlCache<int, int>(TimeSpan.FromMinutes(5));
        const int threads = 8;
        const int opsPerThread = 50_000;
        const int keySpace = 256;

        var workers = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < opsPerThread; i++)
            {
                var key = (t * 31 + i) % keySpace;
                if (i % 3 == 0)
                    cache.Set(key, i);
                else
                    cache.TryGet(key, out _);
            }
        }));

        // Если внутренняя структура повреждена, здесь будет исключение или зависание.
        await Task.WhenAll(workers);

        Assert.InRange(cache.Count, 0, keySpace);
    }
}
