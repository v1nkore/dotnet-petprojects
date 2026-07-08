using Microsoft.EntityFrameworkCore;
using OrderFlow.Shared.Outbox;

namespace OrderFlow.Payments.Data;

public enum PaymentStatus
{
    Succeeded,
    Declined,
}

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? BankTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(b =>
        {
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.HasIndex(p => p.OrderId);
        });

        modelBuilder.AddOutboxEntities();
    }
}
