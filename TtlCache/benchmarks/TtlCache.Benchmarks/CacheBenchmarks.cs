using BenchmarkDotNet.Attributes;

namespace TtlCache.Benchmarks;

/// <summary>
/// Смешанная нагрузка: 90% чтений / 10% записей по ограниченному пространству ключей —
/// типичный профиль кэша. Каждый замер: Threads потоков × OpsPerThread операций.
/// MemoryDiagnoser показывает аллокации, ThreadingDiagnoser — contention (сколько раз
/// потоки реально дрались за блокировку).
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class CacheBenchmarks
{
    private const int OpsPerThread = 200_000;
    private const int KeySpace = 1024;

    [Params(1, 4, 8)]
    public int Threads { get; set; }

    private LockDictionaryCache _lockCache = null!;
    private ConcurrentDictionaryCache _concurrentCache = null!;
    private ReaderWriterLockSlimCache _rwLockCache = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lockCache = new LockDictionaryCache();
        _concurrentCache = new ConcurrentDictionaryCache();
        _rwLockCache = new ReaderWriterLockSlimCache();

        for (var key = 0; key < KeySpace; key++)
        {
            _lockCache.Set(key, key);
            _concurrentCache.Set(key, key);
            _rwLockCache.Set(key, key);
        }
    }

    [Benchmark(Baseline = true)]
    public void LockDictionary() => RunMixedLoad(_lockCache);

    [Benchmark]
    public void ConcurrentDictionary() => RunMixedLoad(_concurrentCache);

    [Benchmark]
    public void ReaderWriterLockSlim() => RunMixedLoad(_rwLockCache);

    private void RunMixedLoad(IBenchCache cache)
    {
        if (Threads == 1)
        {
            Worker(cache, seed: 1);
            return;
        }

        var workers = new Task[Threads];
        for (var t = 0; t < Threads; t++)
        {
            var seed = (uint)(t + 1);
            workers[t] = Task.Run(() => Worker(cache, seed));
        }

        Task.WaitAll(workers);
    }

    private static void Worker(IBenchCache cache, uint seed)
    {
        // xorshift вместо Random: детерминирован, без аллокаций и без блокировок,
        // чтобы генератор случайности не влиял на то, что мы меряем.
        var rng = seed;

        for (var i = 0; i < OpsPerThread; i++)
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;

            var key = (int)(rng % KeySpace);

            if (rng % 10 == 0)
                cache.Set(key, i);        // 10% записей
            else
                cache.TryGet(key, out _); // 90% чтений
        }
    }
}
