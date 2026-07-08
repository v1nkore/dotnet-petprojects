using Microsoft.EntityFrameworkCore;

namespace TestingLab.Domain;

// Домен нарочно маленький, но с тремя видами поведения, которые
// НЕВОЗМОЖНО честно протестировать без реальной БД:
//   1) уникальный индекс (in-memory провайдер его не проверяет)
//   2) SQL-специфика Postgres (date_trunc)
//   3) optimistic concurrency через xmin (системная колонка Postgres)

public class Debt
{
    public int Id { get; set; }
    public required string ContractNumber { get; set; }
    public decimal Principal { get; set; }
    public bool IsClosed { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public uint Version { get; set; } // xmin
}

public class DebtsDbContext(DbContextOptions<DebtsDbContext> options) : DbContext(options)
{
    public DbSet<Debt> Debts => Set<Debt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Debt>(b =>
        {
            b.HasIndex(d => d.ContractNumber).IsUnique(); // бизнес-инвариант живёт в БД
            b.Property(d => d.Principal).HasPrecision(18, 2);
            b.Property(d => d.Version).IsRowVersion();    // маппинг на xmin
        });
    }
}

public sealed class DebtService(DebtsDbContext db)
{
    public async Task<Debt> OpenDebtAsync(string contractNumber, decimal principal)
    {
        var debt = new Debt
        {
            ContractNumber = contractNumber,
            Principal = principal,
            OpenedAtUtc = DateTime.UtcNow,
        };
        db.Debts.Add(debt);
        await db.SaveChangesAsync(); // дубликат договора → DbUpdateException от unique-индекса
        return debt;
    }

    /// <summary>Сумма открытых долгов по месяцам — Postgres-специфичный SQL (date_trunc).</summary>
    public Task<List<MonthlyTotal>> MonthlyTotalsAsync() =>
        db.Database.SqlQuery<MonthlyTotal>($"""
            SELECT date_trunc('month', "OpenedAtUtc") AS "Month", SUM("Principal") AS "Total"
            FROM "Debts"
            WHERE NOT "IsClosed"
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync();
}

public sealed record MonthlyTotal(DateTime Month, decimal Total);
