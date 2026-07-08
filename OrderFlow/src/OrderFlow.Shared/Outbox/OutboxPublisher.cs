using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderFlow.Shared.Outbox;

/// <summary>
/// Вторая половина Outbox: воркер опрашивает таблицу и публикует в Kafka.
/// Генерик по DbContext — один и тот же код работает в Orders и Payments.
///
/// Гарантия — at-least-once: если процесс упадёт между ProduceAsync и SaveChanges,
/// после рестарта сообщение уйдёт ПОВТОРНО. Поэтому обязательна идемпотентность
/// на стороне консьюмера (ProcessedMessage) — дедупликация по MessageId.
/// </summary>
public sealed class OutboxPublisher<TContext>(
    IServiceScopeFactory scopeFactory,
    IProducer<string, string> producer,
    string topic,
    ILogger<OutboxPublisher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxPublisher<{Context}> → топик {Topic}", typeof(TContext).Name, topic);

        using var timer = new PeriodicTimer(PollInterval);
        while (await WaitSafely(timer, stoppingToken))
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Kafka недоступна → лог и следующая попытка через тик.
                // Outbox никуда не денется — в этом и прелесть: очередь переживёт сбой в таблице.
                logger.LogError(ex, "Ошибка публикации outbox, повтор через {Interval}", PollInterval);
            }
        }
    }

    private async Task PublishPendingAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var pending = await db.Set<OutboxMessage>()
            .Where(m => m.PublishedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            // Producer-спан подцепляется к трейсу, СОХРАНЁННОМУ в outbox-строке, —
            // так вся сага (HTTP → outbox → Kafka → консьюмер → банк) остаётся одним трейсом
            ActivityContext.TryParse(message.TraceParent, null, out var parentContext);
            using var activity = Kafka.KafkaTelemetry.Source.StartActivity(
                $"{topic} publish", ActivityKind.Producer, parentContext);
            activity?.SetTag("messaging.system", "kafka");
            activity?.SetTag("messaging.destination.name", topic);
            activity?.SetTag("messaging.message.id", message.Id);

            // Ключ = id агрегата → все события заказа попадают в одну партицию → порядок
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = message.AggregateKey,
                Value = message.ToEnvelope().ToJson(),
                Headers = Kafka.KafkaTelemetry.InjectContext(activity),
            }, ct);

            message.PublishedAtUtc = DateTime.UtcNow;
            // SaveChanges на каждое сообщение, а не на батч: сузить окно
            // «опубликовано, но не помечено» до одного сообщения
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Outbox → {Topic}: {Type} ({MessageId})", topic, message.Type, message.Id);
        }
    }

    private static async ValueTask<bool> WaitSafely(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

public static class KafkaSetup
{
    /// <summary>
    /// Продюсер: singleton на приложение (тяжёлый, потокобезопасный).
    /// EnableIdempotence: брокер дедуплицирует ретраи продюсера —
    /// защита от дублей на ПЕРВОМ плече (сервис → Kafka).
    /// Acks.All: подтверждение от всех реплик, не теряем при падении брокера.
    /// </summary>
    public static IProducer<string, string> CreateProducer(string bootstrapServers) =>
        new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();

    public static IConsumer<string, string> CreateConsumer(string bootstrapServers, string groupId) =>
        new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            // Смещение коммитим ТОЛЬКО после успешной обработки (см. консьюмеры) —
            // упали до коммита → сообщение придёт снова → at-least-once
            EnableAutoCommit = false,
            // При первом запуске группы читаем с начала топика
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
}
