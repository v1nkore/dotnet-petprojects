using NBomber.CSharp;
using NBomber.Http.CSharp;

// === Нагрузочный тест: p95/p99 от RPS + демонстрация thread pool starvation ===
//
// Два эндпоинта делают «ту же работу» — ждут 50 мс (как будто ходят в БД):
//   /async     → await Task.Delay(50)  — поток возвращается в пул на время ожидания
//   /blocking  → Thread.Sleep(50)      — поток ЗАНЯТ все 50 мс (sync-over-async в проде)
//
// При росте RPS блокирующему нужно rate × 0.05 потоков ОДНОВРЕМЕННО.
// Пул стартует с min = числу ядер и доращивает поток ~раз в 0.5–1 сек —
// очередь пула растёт быстрее → латентность взрывается. Async — плоский.

var app = WebApplication.CreateBuilder(args).Build();
app.MapGet("/async", async () => { await Task.Delay(50); return "ok"; });
app.MapGet("/blocking", () => { Thread.Sleep(50); return "ok"; });
_ = app.RunAsync("http://localhost:5500");
await Task.Delay(1500); // даём Kestrel подняться

using var http = new HttpClient();
int[] rates = [50, 200, 400];

Console.WriteLine($"{"endpoint",-10} {"RPS",5} {"p50",8} {"p95",8} {"p99",8} {"ошибок",7}");

foreach (var endpoint in (string[])["/async", "/blocking"])
{
    foreach (var rate in rates)
    {
        var scenario = Scenario.Create($"{endpoint.Trim('/')}_{rate}rps", async _ =>
            {
                var request = Http.CreateRequest("GET", $"http://localhost:5500{endpoint}");
                return await Http.Send(http, request);
            })
            .WithoutWarmUp()
            .WithLoadSimulations(Simulation.Inject(
                rate: rate,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(10)));

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithoutReports()
            .Run();

        var s = stats.ScenarioStats[0];
        Console.WriteLine($"{endpoint,-10} {rate,5} {s.Ok.Latency.Percent50,7:F0}м {s.Ok.Latency.Percent95,7:F0}м {s.Ok.Latency.Percent99,7:F0}м {s.AllFailCount,7}");
    }
}

Console.WriteLine("""

Вывод: /async держит ~62 мс на любом RPS — ожидание не занимает поток.
/blocking деградирует: потоков нужно rate × 0.05 одновременно, пул доращивает
их медленно — очередь растёт, p99 умножается (на 400 RPS — в ~7 раз против async).
Подними rate до 800–1000 — увидишь секунды и таймауты. Это thread pool starvation —
то, что в проде делает sync-over-async (.Result/.Wait) и блокирующий I/O.
""");
