using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Shared.Contracts;

namespace OrderFlow.Shared.Kafka;

/// <summary>
/// Каркас Kafka-консьюмера. Наследник реализует только HandleAsync.
///
/// Ключевые решения:
/// - Consume() — БЛОКИРУЮЩИЙ вызов, поэтому цикл живёт на выделенном потоке
///   (TaskCreationOptions.LongRunning), а не ест поток из пула.
/// - EnableAutoCommit=false + Commit(cr) ПОСЛЕ обработки = at-least-once:
///   упали до коммита → после рестарта сообщение придёт снова.
///   Отсюда обязательная идемпотентность обработчика (таблица ProcessedMessage).
/// - Сознательный sync-over-async (.GetAwaiter().GetResult()): на выделенном
///   потоке без SynchronizationContext deadlock невозможен — тот редкий случай,
///   когда так можно.
/// </summary>
public abstract class KafkaConsumerService(
    string bootstrapServers,
    string topic,
    string groupId,
    ILogger logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(
            () => ConsumeLoop(stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private void ConsumeLoop(CancellationToken ct)
    {
        using var consumer = Outbox.KafkaSetup.CreateConsumer(bootstrapServers, groupId);
        consumer.Subscribe(topic);
        logger.LogInformation("Консьюмер группы {Group} подписан на {Topic}", groupId, topic);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(ct);
                var envelope = EventEnvelope.FromJson(result.Message.Value);

                // Consumer-спан привязывается к контексту из заголовков сообщения —
                // обработка становится продолжением трейса, начатого HTTP-запросом
                var parent = KafkaTelemetry.ExtractContext(result.Message.Headers);
                using (var activity = KafkaTelemetry.Source.StartActivity(
                           $"{topic} process", System.Diagnostics.ActivityKind.Consumer, parent.ActivityContext))
                {
                    activity?.SetTag("messaging.system", "kafka");
                    activity?.SetTag("messaging.kafka.consumer.group", groupId);

                    HandleAsync(envelope, ct).GetAwaiter().GetResult();
                }

                // Коммит только после успешной обработки. Упрощение учебного кода:
                // при исключении из HandleAsync мы логируем и продолжаем со следующего
                // сообщения — в проде здесь были бы retry с backoff и dead-letter topic.
                consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                logger.LogError(ex, "Ошибка Kafka: {Reason}", ex.Error.Reason);
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка обработки сообщения из {Topic}", topic);
                Thread.Sleep(1000);
            }
        }

        // Close (а не только Dispose): корректно покидаем consumer group,
        // не дожидаясь session timeout — rebalance произойдёт мгновенно
        consumer.Close();
    }

    protected abstract Task HandleAsync(EventEnvelope envelope, CancellationToken ct);
}
