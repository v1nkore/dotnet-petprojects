using System.Diagnostics;
using EfCoreLab.App.Data;
using Microsoft.EntityFrameworkCore;

namespace EfCoreLab.App.Scenarios;

/// <summary>
/// N+1: 1 запрос за родителями + по запросу на каждого ребёнка.
/// Три версии одного и того же чтения «должники и сумма их долгов»:
///   плохая (lazy loading) → рабочая (Include) → лучшая (проекция).
/// </summary>
public static class NPlusOneScenario
{
    public static async Task RunAsync(
        Func<bool, CollectionsDbContext> createContext,
        QueryCountingInterceptor counter)
    {
        Console.WriteLine("\n═══ Сценарий 1: N+1 ═══\n");

        // --- ПЛОХО: lazy loading. Код выглядит невинно — обращение к d.Debts
        // как к обычному свойству. Прокси в этот момент молча ходит в базу.
        {
            await using var db = createContext(/* lazyLoading: */ true);
            counter.Reset();
            var sw = Stopwatch.StartNew();

            var debtors = await db.Debtors.ToListAsync();          // 1 запрос
            var report = debtors
                .Select(d => (d.Name, Total: d.Debts.Sum(x => x.Principal))) // + N запросов!
                .ToList();

            sw.Stop();
            Console.WriteLine($"[lazy loading]  запросов: {counter.Count,4} · {sw.ElapsedMilliseconds} мс · строк отчёта: {report.Count}");
        }

        // --- РАБОЧЕ: Include. Один запрос с JOIN, но тянем ВСЕ колонки долгов,
        // а нужна только сумма — и всё это грузится в change tracker.
        {
            await using var db = createContext(false);
            counter.Reset();
            var sw = Stopwatch.StartNew();

            var debtors = await db.Debtors
                .Include(d => d.Debts)
                .AsNoTracking()
                .ToListAsync();
            var report = debtors
                .Select(d => (d.Name, Total: d.Debts.Sum(x => x.Principal)))
                .ToList();

            sw.Stop();
            Console.WriteLine($"[Include]       запросов: {counter.Count,4} · {sw.ElapsedMilliseconds} мс · строк отчёта: {report.Count}");
        }

        // --- ЛУЧШЕ ВСЕГО: проекция. Сумма считается В БАЗЕ (GROUP BY),
        // по сети едут только имя и число. Материализуется анонимный тип — трекать нечего.
        {
            await using var db = createContext(false);
            counter.Reset();
            var sw = Stopwatch.StartNew();

            var report = await db.Debtors
                .Select(d => new { d.Name, Total = d.Debts.Sum(x => x.Principal) })
                .ToListAsync();

            sw.Stop();
            Console.WriteLine($"[проекция]      запросов: {counter.Count,4} · {sw.ElapsedMilliseconds} мс · строк отчёта: {report.Count}");
        }

        Console.WriteLine("""

        Вывод: lazy loading прячет N+1 за невинным обращением к свойству.
        Include чинит число запросов, проекция — ещё и объём данных и change tracking.
        """);
    }
}
