using TtlCache.Core;

// Демо на реальных часах: короткие TTL, чтобы увидеть жизненный цикл записи глазами.
Console.WriteLine("=== TtlCache demo ===\n");

using var cache = new TtlCache<string, string>(
    defaultTtl: TimeSpan.FromSeconds(2),
    cleanupInterval: TimeSpan.FromSeconds(1));

// 1. Обычный цикл: положили — прочитали
cache.Set("user:1", "Alice");
cache.Set("user:2", "Bob", ttl: TimeSpan.FromSeconds(10)); // персональный TTL

Print(cache, "user:1"); // hit
Print(cache, "user:2"); // hit

// 2. Ждём, пока дефолтный TTL истечёт
Console.WriteLine("\n...ждём 3 секунды (default TTL = 2s)...\n");
await Task.Delay(TimeSpan.FromSeconds(3));

Print(cache, "user:1"); // miss — протух
Print(cache, "user:2"); // hit  — у него TTL 10s

// 3. GetOrAdd: промах прозрачно наполняет кэш
var plan = cache.GetOrAdd("plan:1", key =>
{
    Console.WriteLine($"  [factory] дорогое вычисление для '{key}'...");
    return "premium";
});
Console.WriteLine($"GetOrAdd -> {plan}");
plan = cache.GetOrAdd("plan:1", _ => "не должно вызваться");
Console.WriteLine($"GetOrAdd (повторно, из кэша) -> {plan}");

// 4. Фоновый свипер убирает протухшее даже без чтений
cache.Set("orphan", "никто меня не прочитает", TimeSpan.FromSeconds(1));
Console.WriteLine($"\nCount до свипера: {cache.Count}");
await Task.Delay(TimeSpan.FromSeconds(2.5));
Console.WriteLine($"Count после свипера: {cache.Count} (orphan убран фоном)");

// 5. Статистика
var (hits, misses) = cache.Stats;
Console.WriteLine($"\nСтатистика: hits={hits}, misses={misses}, hit ratio={cache.Stats.HitRatio:P0}");

static void Print(TtlCache<string, string> cache, string key)
{
    var text = cache.TryGet(key, out var value) ? $"HIT  -> {value}" : "MISS";
    Console.WriteLine($"TryGet(\"{key}\"): {text}");
}
