using Microsoft.EntityFrameworkCore;
using OrderFlow.Shared.Outbox;

namespace OrderFlow.Orders.Data;

public enum OrderStatus
{
    Pending,    // создан, ждём оплату
    Completed,  // оплата прошла
    Cancelled,  // компенсация: оплата не прошла
}

public class Order
{
    public Guid Id { get; set; }
    public required string CustomerEmail { get; set; }
    public decimal Amount { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.Property(o => o.CustomerEmail).HasMaxLength(200);
            b.Property(o => o.Amount).HasPrecision(18, 2);
        });

        // Outbox + реестр обработанных сообщений — общая схема из Shared
        modelBuilder.AddOutboxEntities();
    }
}
