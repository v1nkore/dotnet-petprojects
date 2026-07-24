using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderFlow.Payments;
using OrderFlow.Payments.Data;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Outbox;
using Polly;

var builder = Host.CreateApplicationBuilder(args);

const string kafka = "localhost:9092";
const string connectionString =
    "Host=localhost;Port=5456;Database=payments;Username=orderflow;Password=orderflow";

builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseNpgsql(connectionString));

// OpenTelemetry: у воркера нет HTTP-сервера — только исходящий HttpClient (банк),
// Kafka-спаны и SQL. Трейсы уходят в Jaeger по OTLP.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("payments"))
    .WithTracing(t => t
        .AddHttpClientInstrumentation() // спан на каждый вызов банка (виден каждый Polly-ретрай!)
        .AddSource(OrderFlow.Shared.Kafka.KafkaTelemetry.SourceName)
        .AddSource("Npgsql")
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")));

// === Polly (v8) поверх HttpClient к «банку» ===
// Стратегии выполняются СВЕРХУ ВНИЗ, порядок — это семантика:
//   1. total timeout  — потолок на всю операцию со всеми ретраями
//   2. retry          — до 4 попыток с экспоненциальным backoff и джиттером
//   3. circuit breaker— если банк массово падает, перестаём его добивать
//   4. attempt timeout— потолок на ОДНУ попытку (ловит «зависшие» запросы)
builder.Services.AddHttpClient<BankClient>(c => c.BaseAddress = new Uri("http://localhost:5301"))
    .AddResiliencePipeline("bank", pipeline =>
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));

        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 4,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true, // без джиттера все инстансы ретраят синхронно — retry storm
        });

        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(10),
            FailureRatio = 0.9,      // >90% неудач за окно...
            MinimumThroughput = 10,  // ...при минимум 10 запросах
            BreakDuration = TimeSpan.FromSeconds(5),
        });

        pipeline.AddTimeout(TimeSpan.FromSeconds(1));
    });

builder.Services.AddSingleton(_ => KafkaSetup.CreateProducer(kafka));

// Входящая сторона: слушаем заказы
builder.Services.AddSingleton<IHostedService>(sp => new OrderEventsConsumer(
    kafka,
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<OrderEventsConsumer>>()));

// Исходящая сторона: публикуем исходы оплат из собственного outbox
builder.Services.AddSingleton<IHostedService>(sp => new OutboxPublisher<PaymentsDbContext>(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<Confluent.Kafka.IProducer<string, string>>(),
    KafkaTopics.PaymentEvents,
    sp.GetRequiredService<ILogger<OutboxPublisher<PaymentsDbContext>>>()));

var host = builder.Build();

using (var scope = host.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.EnsureCreated();

host.Run();
