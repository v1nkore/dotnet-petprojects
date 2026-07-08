using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TestingLab.Domain;

namespace TestingLab.Tests;

/// <summary>
/// Фикстура: ОДИН контейнер Postgres на класс тестов (IClassFixture).
/// Контейнер на каждый тест — слишком дорого (~2–3 сек старта);
/// на класс + очистка таблиц между тестами — золотая середина.
/// Testcontainers сам скачает образ, найдёт свободный порт и убьёт контейнер
/// после тестов (даже при падении процесса — через контейнер-жнец ryuk).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public DebtsDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DebtsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    /// <summary>Изоляция тестов: чистим данные, не пересоздавая ни контейнер, ни схему.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE \"Debts\" RESTART IDENTITY");
    }
}

public class DebtServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync(); // перед КАЖДЫМ тестом

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OpenDebt_PersistsAndReadsBack()
    {
        await using (var db = fixture.CreateContext())
            await new DebtService(db).OpenDebtAsync("К-001", 150_000m);

        // Читаем ДРУГИМ контекстом: проверяем базу, а не change tracker
        await using var verify = fixture.CreateContext();
        var saved = await verify.Debts.SingleAsync();
        Assert.Equal("К-001", saved.ContractNumber);
        Assert.Equal(150_000m, saved.Principal);
    }

    [Fact]
    public async Task DuplicateContractNumber_ThrowsFromUniqueIndex()
    {
        // In-memory провайдер этот тест ПРОХОДИТ МОЛЧА (индексы не проверяет) —
        // главный аргумент против него: зелёный тест, красный прод
        await using var db = fixture.CreateContext();
        var service = new DebtService(db);
        await service.OpenDebtAsync("К-002", 100m);

        await using var db2 = fixture.CreateContext();
        await Assert.ThrowsAsync<DbUpdateException>(
            () => new DebtService(db2).OpenDebtAsync("К-002", 200m));
    }

    [Fact]
    public async Task MonthlyTotals_GroupsByMonth_UsingPostgresSql()
    {
        // date_trunc — сырой Postgres-SQL: in-memory провайдер не исполняет SQL вовсе
        await using (var db = fixture.CreateContext())
        {
            db.Debts.AddRange(
                new Debt { ContractNumber = "К-101", Principal = 100m, OpenedAtUtc = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc) },
                new Debt { ContractNumber = "К-102", Principal = 200m, OpenedAtUtc = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc) },
                new Debt { ContractNumber = "К-103", Principal = 999m, OpenedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
                new Debt { ContractNumber = "К-104", Principal = 500m, OpenedAtUtc = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), IsClosed = true });
            await db.SaveChangesAsync();
        }

        await using var query = fixture.CreateContext();
        var totals = await new DebtService(query).MonthlyTotalsAsync();

        Assert.Equal(2, totals.Count);
        Assert.Equal(300m, totals[0].Total);  // май: 100 + 200
        Assert.Equal(999m, totals[1].Total);  // июнь: закрытый не считается
    }

    [Fact]
    public async Task ConcurrentUpdate_ThrowsConcurrencyException_ViaXmin()
    {
        await using (var db = fixture.CreateContext())
            await new DebtService(db).OpenDebtAsync("К-777", 1000m);

        // Два «оператора» читают одну строку
        await using var db1 = fixture.CreateContext();
        await using var db2 = fixture.CreateContext();
        var first = await db1.Debts.SingleAsync();
        var second = await db2.Debts.SingleAsync();

        first.Principal = 2000m;
        await db1.SaveChangesAsync();

        second.IsClosed = true; // не знает об изменении → xmin не совпадёт
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
}
