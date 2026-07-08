using System.Collections.Concurrent;
using System.Diagnostics;
using StackExchange.Redis;

namespace RedisLab.App;

public static class Scenarios
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    // ═══ 1. Cache-aside: базовый паттерн ═══
    // Читаем кэш → промах → идём в БД → кладём в кэш с TTL → отдаём.
    // Кэш пассивен, всю оркестрацию делает приложение (в отличие от read-through,
    // где за наполнение отвечает сама кэш-прослойка).
    public static async Task CacheAsideAsync(IDatabase redis, SlowDatabase db)
    {
        Console.WriteLine("\n═══ Сценарий 1: cache-aside ═══\n");
        db.ResetCalls();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var sw = Stopwatch.StartNew();
            var value = await GetCacheAsideAsync(redis, db, id: 7);
            sw.Stop();
            Console.WriteLine($"запрос {attempt}: {sw.ElapsedMilliseconds,4} мс · походов в БД всего: {db.Calls} · {value[..40]}...");
        }

        Console.WriteLine("\nВывод: первый запрос платит полную цену (miss), остальные — ~1 мс из Redis.");
    }

    private static async Task<string> GetCacheAsideAsync(IDatabase redis, SlowDatabase db, int id)
    {
        var key = $"debtor:{id}";

        var cached = await redis.StringGetAsync(key);
        if (!cached.IsNull)
            return cached!;

        var value = await db.LoadDebtorAsync(id);
        // SET с TTL одним вызовом — не SET + EXPIRE (двумя командами можно
        // получить ключ-бессмертник, если упасть между ними)
        await redis.StringSetAsync(key, value, Ttl);
        return value;
    }

    // ═══ 2. Cache stampede ═══
    // Горячий ключ протух → сотня запросов одновременно ловит miss →
    // сотня параллельных походов в БД за ОДНИМ И ТЕМ ЖЕ. БД складывается
    // ровно в момент, когда кэш «должен был спасать».
    public static async Task StampedeAsync(IDatabase redis, SlowDatabase db)
    {
        Console.WriteLine("\n═══ Сценарий 2: cache stampede (50 конкурентных промахов) ═══\n");

        // --- Наивный cache-aside: все 50 идут в БД
        db.ResetCalls();
        await redis.KeyDeleteAsync("debtor:100");
        await RunParallel(50, () => GetCacheAsideAsync(redis, db, 100));
        Console.WriteLine($"[наивный cache-aside]     походов в БД: {db.Calls,3} (должен быть 1!)");

        // --- Single-flight внутри процесса: SemaphoreSlim на ключ + double-check
        db.ResetCalls();
        await redis.KeyDeleteAsync("debtor:100");
        await RunParallel(50, () => GetSingleFlightAsync(redis, db, 100));
        Console.WriteLine($"[single-flight в памяти]  походов в БД: {db.Calls,3}");

        // --- Распределённый лок: SET NX PX — работает и МЕЖДУ инстансами сервиса
        db.ResetCalls();
        await redis.KeyDeleteAsync("debtor:100");
        await RunParallel(50, () => GetWithDistributedLockAsync(redis, db, 100));
        Console.WriteLine($"[распределённый лок]      походов в БД: {db.Calls,3}");

        Console.WriteLine("""

        Вывод: наивный cache-aside при конкурентном промахе — это 50× удар по БД.
        SemaphoreSlim решает внутри одного процесса, SET NX (distributed lock) —
        между всеми инстансами сервиса. Третий путь без локов — early refresh:
        обновлять значение ДО истечения TTL фоновым процессом.
        """);
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();

    private static async Task<string> GetSingleFlightAsync(IDatabase redis, SlowDatabase db, int id)
    {
        var key = $"debtor:{id}";

        var cached = await redis.StringGetAsync(key);
        if (!cached.IsNull)
            return cached!;

        var gate = KeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Double-check: пока мы ждали семафор, первый поток уже мог наполнить кэш
            cached = await redis.StringGetAsync(key);
            if (!cached.IsNull)
                return cached!;

            var value = await db.LoadDebtorAsync(id);
            await redis.StringSetAsync(key, value, Ttl);
            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<string> GetWithDistributedLockAsync(IDatabase redis, SlowDatabase db, int id)
    {
        var key = $"debtor:{id}";
        var lockKey = $"lock:{key}";
        var lockToken = Guid.NewGuid().ToString(); // чей лок — того и право снять

        while (true)
        {
            var cached = await redis.StringGetAsync(key);
            if (!cached.IsNull)
                return cached!;

            // SET NX PX — атомарно «поставь, если нет, с автопротуханием».
            // TTL на локе обязателен: упавший держатель не заблокирует ключ навечно.
            if (await redis.StringSetAsync(lockKey, lockToken, TimeSpan.FromSeconds(5), When.NotExists))
            {
                try
                {
                    var value = await db.LoadDebtorAsync(id);
                    await redis.StringSetAsync(key, value, Ttl);
                    return value;
                }
                finally
                {
                    // Снимаем ТОЛЬКО свой лок — compare-and-delete через Lua (атомарно).
                    // Иначе: наш лок протух, взял другой, а мы удалили ЕГО лок.
                    await redis.ScriptEvaluateAsync("""
                        if redis.call('get', KEYS[1]) == ARGV[1]
                        then return redis.call('del', KEYS[1])
                        else return 0 end
                        """, [(RedisKey)lockKey], [(RedisValue)lockToken]);
                }
            }

            // Лок занят: не долбим БД, ждём и перечитываем кэш
            await Task.Delay(50);
        }
    }

    // ═══ 3. Инвалидация при изменении данных ═══
    public static async Task InvalidationAsync(IDatabase redis, SlowDatabase db)
    {
        Console.WriteLine("\n═══ Сценарий 3: инвалидация ═══\n");

        var key = "debtor:7";
        var before = await GetCacheAsideAsync(redis, db, 7);
        Console.WriteLine($"в кэше:      {before}");

        // «Обновили» данные в БД. Кэш об этом не знает — читатели видят старое до TTL.
        Console.WriteLine("...данные в БД изменились, кэш не тронут...");
        var stale = await redis.StringGetAsync(key);
        Console.WriteLine($"из кэша:     {stale} ← ПРОТУХШЕЕ");

        // Правильный путь записи: обновить БД → УДАЛИТЬ ключ (не перезаписать!).
        // Удаление проще и безопаснее write-through: нет гонки «две записи
        // перезаписали кэш в разном порядке», следующий читатель наполнит кэш сам.
        await redis.KeyDeleteAsync(key);
        var fresh = await GetCacheAsideAsync(redis, db, 7);
        Console.WriteLine($"после DEL:   {fresh} ← перечитано из БД (loadedAt изменился)");

        Console.WriteLine("""

        Вывод: пара «обновление БД + DEL ключа» — рабочая лошадка инвалидации.
        TTL остаётся страховкой на случай пропущенной инвалидации.
        Полная согласованность кэша и БД недостижима — выбираем окно рассинхрона.
        """);
    }

    private static Task RunParallel(int count, Func<Task<string>> action) =>
        Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(action)));
}
