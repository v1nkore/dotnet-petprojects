using Microsoft.EntityFrameworkCore;
using OrderFlow.Shared.Contracts;

namespace OrderFlow.Shared.Outbox;

/// <summary>
/// Строка Outbox. Пишется В ОДНОЙ ТРАНЗАКЦИИ с бизнес-данными — в этом весь смысл
/// паттерна: невозможна ситуация «заказ сохранён, а событие потерялось» (или наоборот).
/// Публикацией занимается отдельный воркер (OutboxPublisher).
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }                 // он же MessageId конверта
    public required string AggregateKey { get; set; } // ключ партиционирования Kafka
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; } // null = ещё не опубликовано

    // Контекст трейса, в котором родилось событие. Без этого трейс РВЁТСЯ:
    // публикатор работает в фоновом цикле, у него нет родительского Activity
    // от HTTP-запроса — и «publish» начинал бы новый, несвязанный трейс.
    public string? TraceParent { get; set; }

    public static OutboxMessage From<T>(T @event, string aggregateKey) where T : class
    {
        var envelope = EventEnvelope.Wrap(@event);
        return new OutboxMessage
        {
            Id = envelope.MessageId,
            AggregateKey = aggregateKey,
            Type = envelope.Type,
            Payload = envelope.Payload,
            OccurredAtUtc = envelope.OccurredAtUtc,
            TraceParent = System.Diagnostics.Activity.Current?.Id, // W3C traceparent
        };
    }

    public EventEnvelope ToEnvelope() => new(Id, Type, Payload, OccurredAtUtc);
}

/// <summary>
/// Реестр обработанных сообщений — вторая половина идемпотентности.
/// Вставляется В ОДНОЙ ТРАНЗАКЦИИ с бизнес-эффектом обработки:
/// пришёл дубликат → PK-конфликт по MessageId → эффекта дважды не будет.
/// </summary>
public class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

public static class OutboxModelBuilder
{
    /// <summary>Общая схема outbox/inbox для любого сервисного DbContext.</summary>
    public static void AddOutboxEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(m => m.Id);
            // Частичный индекс под запрос воркера «дай неопубликованные по порядку»
            b.HasIndex(m => m.OccurredAtUtc).HasFilter("\"PublishedAtUtc\" IS NULL");
        });

        modelBuilder.Entity<ProcessedMessage>(b => b.HasKey(m => m.MessageId));
    }
}
