using EfCoreLab.App.Data;
using EfCoreLab.App.Scenarios;
using Microsoft.EntityFrameworkCore;

// Postgres из docker-compose.yml: docker compose up -d
const string connectionString =
    "Host=localhost;Port=5455;Database=eflab;Username=eflab;Password=eflab";

var counter = new QueryCountingInterceptor();

// Фабрика контекстов: каждый сценарий получает СВЕЖИЙ контекст —
// DbContext задуман короткоживущим (unit of work), а не объектом на всё приложение
CollectionsDbContext CreateContext(bool lazyLoading)
{
    var builder = new DbContextOptionsBuilder<CollectionsDbContext>()
        .UseNpgsql(connectionString)
        .AddInterceptors(counter);

    if (lazyLoading)
        builder.UseLazyLoadingProxies();

    return new CollectionsDbContext(builder.Options);
}

Console.WriteLine("=== EfCoreLab: подготовка базы ===");
await Seed.EnsureSeededAsync(CreateContext(false));

await NPlusOneScenario.RunAsync(CreateContext, counter);
await ChangeTrackingScenario.RunAsync(CreateContext);
await ConcurrencyScenario.RunAsync(CreateContext);
await IndexingScenario.RunAsync(connectionString);

Console.WriteLine("\n=== Готово ===");
