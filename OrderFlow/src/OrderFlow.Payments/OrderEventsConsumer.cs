using Microsoft.EntityFrameworkCore;
using OrderFlow.Payments.Data;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Kafka;
using OrderFlow.Shared.Outbox;
using Polly.CircuitBreaker;

namespace OrderFlow.Payments;

/// <summary>
/// Сага, шаг 2: слушаем OrderCreated, пытаемся списать деньги, публикуем исход.
/// Исход (успех ИЛИ провал) снова уходит через Outbox — паттерн сквозной для всей саги.
/// </summary>
public sealed class OrderEventsConsumer(
    string bootstrapServers,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderEventsConsumer> logger)
    : KafkaConsumerService(bootstrapServers, KafkaTopics.OrderEvents, "payments-service", logger)
{
    private readonly ILogger<OrderEventsConsumer> _logger = logger;

    protected override async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Type != nameof(OrderCreated))
            return;

        var order = envelope.Unwrap<OrderCreated>();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        // Идемпотентность ДО побочных эффектов: повторная доставка не должна
        // привести ко второму списанию — это худший из возможных багов в платежах
        if (await db.Set<ProcessedMessage>().AnyAsync(m => m.MessageId == envelope.MessageId, ct))
        {
            _logger.LogInformation("Дубликат OrderCreated {OrderId} — пропущен", order.OrderId);
            return;
        }

        var bank = scope.ServiceProvider.GetRequiredService<BankClient>();

        Payment payment;
        object resultEvent;
        try
        {
            var result = await bank.ChargeAsync(order.OrderId, order.Amount, ct);

            (payment, resultEvent) = result switch
            {
                BankChargeResult.Success s => MakeSuccess(order, s.TransactionId),
                BankChargeResult.Declined d => MakeFailure(order, d.Reason),
                _ => throw new InvalidOperationException(),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            // Банк не ответил даже после ретраев (или circuit разомкнут).
            // Учебное упрощение: завершаем сагу отказом, чтобы она всегда сходилась.
            // В проде здесь выбор: отложенный повтор (retry topic) или dead-letter + алерт.
            _logger.LogError(ex, "Банк недоступен для заказа {OrderId}", order.OrderId);
            (payment, resultEvent) = MakeFailure(order, "Банк недоступен");
        }

        db.Payments.Add(payment);
        db.Set<OutboxMessage>().Add(resultEvent switch
        {
            PaymentSucceeded e => OutboxMessage.From(e, order.OrderId.ToString()),
            PaymentFailed e => OutboxMessage.From(e, order.OrderId.ToString()),
            _ => throw new InvalidOperationException(),
        });
        db.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            MessageId = envelope.MessageId,
            ProcessedAtUtc = DateTime.UtcNow,
        });

        // Платёж + исходящее событие + отметка идемпотентности = ОДНА транзакция
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Заказ {OrderId}: {Result}", order.OrderId, payment.Status);
    }

    private static (Payment, object) MakeSuccess(OrderCreated order, string transactionId)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.OrderId,
            Amount = order.Amount,
            Status = PaymentStatus.Succeeded,
            BankTransactionId = transactionId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        return (payment, new PaymentSucceeded(order.OrderId, payment.Id, transactionId));
    }

    private static (Payment, object) MakeFailure(OrderCreated order, string reason) =>
        (new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.OrderId,
            Amount = order.Amount,
            Status = PaymentStatus.Declined,
            FailureReason = reason,
            CreatedAtUtc = DateTime.UtcNow,
        }, new PaymentFailed(order.OrderId, reason));
}
