using System.Text.Json;

namespace OrderFlow.Shared.Contracts;

// События саги — контракт между сервисами. В реале они живут в отдельном
// versioned-пакете: событие, однажды опубликованное, менять нельзя (только добавлять поля).

public sealed record OrderCreated(Guid OrderId, decimal Amount, string CustomerEmail);

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId, string BankTransactionId);

public sealed record PaymentFailed(Guid OrderId, string Reason);

/// <summary>
/// Конверт сообщения. MessageId — основа идемпотентности консьюмера:
/// Kafka гарантирует at-least-once, значит дубликаты — вопрос «когда», а не «если».
/// Type — имя типа события, по нему консьюмер решает, как десериализовать Payload.
/// </summary>
public sealed record EventEnvelope(Guid MessageId, string Type, string Payload, DateTime OccurredAtUtc)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EventEnvelope Wrap<T>(T @event) where T : class =>
        new(Guid.NewGuid(), typeof(T).Name, JsonSerializer.Serialize(@event, Json), DateTime.UtcNow);

    public T Unwrap<T>() where T : class =>
        JsonSerializer.Deserialize<T>(Payload, Json)
        ?? throw new InvalidOperationException($"Не удалось десериализовать {Type}");

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static EventEnvelope FromJson(string json) =>
        JsonSerializer.Deserialize<EventEnvelope>(json, Json)
        ?? throw new InvalidOperationException("Пустой конверт");
}

public static class KafkaTopics
{
    // По топику на агрегат-источник. Ключ сообщения = OrderId: Kafka гарантирует
    // порядок только внутри партиции, а ключ определяет партицию — значит,
    // все события одного заказа идут строго по порядку.
    public const string OrderEvents = "orders.events";
    public const string PaymentEvents = "payments.events";
}
