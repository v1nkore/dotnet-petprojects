using System.Diagnostics;
using EfCoreLab.App.Data;
using Microsoft.EntityFrameworkCore;

namespace EfCoreLab.App.Scenarios;

/// <summary>
/// Change tracking: цена по умолчанию и когда её выключать.
/// Каждая затреканная сущность — snapshot всех значений + запись в identity map;
/// SaveChanges сравнивает snapshot с текущим состоянием (detect changes).
/// Для read-only сценариев (отчёты, API-чтения) это чистые накладные расходы.
/// </summary>
public static class ChangeTrackingScenario
{
    public static async Task RunAsync(Func<bool, CollectionsDbContext> createContext)
    {
        Console.WriteLine("\n═══ Сценарий 2: change tracking и AsNoTracking ═══\n");

        {
            await using var db = createContext(false);
            var sw = Stopwatch.StartNew();
            var tracked = await db.Payments.Take(50_000).ToListAsync();
            sw.Stop();
            Console.WriteLine($"[с трекингом]   {tracked.Count} строк · {sw.ElapsedMilliseconds} мс · " +
                              $"в change tracker'е: {db.ChangeTracker.Entries().Count()} сущностей");
        }

        {
            await using var db = createContext(false);
            var sw = Stopwatch.StartNew();
            var untracked = await db.Payments.AsNoTracking().Take(50_000).ToListAsync();
            sw.Stop();
            Console.WriteLine($"[AsNoTracking]  {untracked.Count} строк · {sw.ElapsedMilliseconds} мс · " +
                              $"в change tracker'е: {db.ChangeTracker.Entries().Count()} сущностей");
        }

        // Identity map: два запроса одной строки С трекингом → один и тот же объект
        {
            await using var db = createContext(false);
            var first = await db.Debts.FirstAsync();
            var second = await db.Debts.FirstAsync();
            Console.WriteLine($"\nIdentity map: два FirstAsync() вернули один объект? {ReferenceEquals(first, second)}");
        }

        Console.WriteLine("""

        Вывод: AsNoTracking для любого чтения без последующего SaveChanges.
        Identity map — приятный побочный эффект трекинга: одна строка = один объект в контексте.
        """);
    }
}
