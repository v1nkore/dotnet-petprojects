using Microsoft.EntityFrameworkCore;
using OrderFlow.Orders.Data;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Kafka;
using OrderFlow.Shared.Outbox;

namespace OrderFlow.Orders.Consumers;

/// <summary>
/// Замыкание саги на стороне Orders: слушаем результаты оплаты.
/// PaymentSucceeded → заказ Completed.
/// PaymentFailed   → заказ Cancelled — это и есть КОМПЕНСИРУЮЩАЯ ТРАНЗАКЦИЯ:
/// мы не «откатываем» создание заказа (распределённого rollback'а не существует),
/// а применяем новое действие, отменяющее бизнес-эффект предыдущего.
/// </summary>
public sealed class PaymentEventsConsumer(
    string bootstrapServers,
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentEventsConsumer> logger)
    : KafkaConsumerService(bootstrapServers, KafkaTopics.PaymentEvents, "orders-service", logger)
{
    private readonly ILogger<PaymentEventsConsumer> _logger = logger;

    protected override async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // Идемпотентность: дубликат (at-least-once!) видим по реестру и молча выходим
        if (await db.Set<ProcessedMessage>().AnyAsync(m => m.MessageId == envelope.MessageId, ct))
        {
            _logger.LogInformation("Дубликат {MessageId} — пропущен", envelope.MessageId);
            return;
        }

        switch (envelope.Type)
        {
            case nameof(PaymentSucceeded):
            {
                var e = envelope.Unwrap<PaymentSucceeded>();
                var order = await db.Orders.FirstAsync(o => o.Id == e.OrderId, ct);
                order.Status = OrderStatus.Completed;
                _logger.LogInformation("Заказ {OrderId} оплачен → Completed", e.OrderId);
                break;
            }
            case nameof(PaymentFailed):
            {
                var e = envelope.Unwrap<PaymentFailed>();
                var order = await db.Orders.FirstAsync(o => o.Id == e.OrderId, ct);
                order.Status = OrderStatus.Cancelled; // компенсация
                _logger.LogWarning("Заказ {OrderId} отменён: {Reason}", e.OrderId, e.Reason);
                break;
            }
            default:
                _logger.LogWarning("Неизвестный тип события {Type} — пропущен", envelope.Type);
                return;
        }

        // Смена статуса и отметка «обработано» — в одной транзакции (один SaveChanges).
        // Упадём до коммита → Kafka передоставит → реестр пуст → обработаем заново. Эффект один.
        db.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            MessageId = envelope.MessageId,
            ProcessedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
