using Microsoft.EntityFrameworkCore;

namespace EfCoreLab.App.Data;

public class CollectionsDbContext(DbContextOptions<CollectionsDbContext> options) : DbContext(options)
{
    public DbSet<Debtor> Debtors => Set<Debtor>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Debtor>(b =>
        {
            b.Property(d => d.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Debt>(b =>
        {
            // uint + IsRowVersion() у Npgsql = маппинг на системную колонку xmin.
            // Каждый UPDATE строки в Postgres меняет её xmin автоматически —
            // идеальный бесплатный токен для optimistic concurrency.
            b.Property(d => d.Version).IsRowVersion();
            b.Property(d => d.Principal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Payment>(b =>
        {
            b.Property(p => p.Amount).HasPrecision(18, 2);
            // Индексов на (Status, PaidAt) намеренно НЕТ — их создаёт
            // IndexingScenario, чтобы показать план запроса до и после
        });
    }
}
