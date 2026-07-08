using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderFlow.Orders.Consumers;
using OrderFlow.Orders.Data;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Outbox;

var builder = WebApplication.CreateBuilder(args);

const string kafka = "localhost:9092";
const string connectionString =
    "Host=localhost;Port=5456;Database=orders;Username=orderflow;Password=orderflow";

builder.Services.AddDbContext<OrdersDbContext>(o => o.UseNpgsql(connectionString));

// Статусы в ответах строками ("Completed"), а не числами
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// === OpenTelemetry: трейсы → Jaeger (OTLP), метрики → Prometheus (/metrics) ===
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders")) // имя сервиса в Jaeger
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()                        // спан на каждый HTTP-запрос
        .AddSource(OrderFlow.Shared.Kafka.KafkaTelemetry.SourceName) // наши Kafka-спаны
        .AddSource("Npgsql")                                   // Npgsql публикует спаны SQL сам
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")))
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation() // http.server.request.duration и др.
        .AddRuntimeInstrumentation()    // GC, пул потоков, аллокации
        .AddPrometheusExporter());

// Продюсер — singleton: тяжёлый объект с собственными потоками и буферами, потокобезопасен
builder.Services.AddSingleton(_ => KafkaSetup.CreateProducer(kafka));

// Outbox-воркер: вычитывает свою таблицу и публикует в orders.events
builder.Services.AddSingleton<IHostedService>(sp => new OutboxPublisher<OrdersDbContext>(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<Confluent.Kafka.IProducer<string, string>>(),
    KafkaTopics.OrderEvents,
    sp.GetRequiredService<ILogger<OutboxPublisher<OrdersDbContext>>>()));

// Обратная половина саги: слушаем результаты оплаты
builder.Services.AddSingleton<IHostedService>(sp => new PaymentEventsConsumer(
    kafka,
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<PaymentEventsConsumer>>()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreated();

// === Сага, шаг 1: создание заказа ===
// Заказ и событие OrderCreated пишутся ОДНИМ SaveChanges — EF Core оборачивает
// его в транзакцию автоматически. Это и есть Outbox: событие не может потеряться,
// если заказ сохранился, и наоборот.
app.MapPost("/orders", async (CreateOrderRequest request, OrdersDbContext db) =>
{
    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerEmail = request.CustomerEmail,
        Amount = request.Amount,
        Status = OrderStatus.Pending,
        CreatedAtUtc = DateTime.UtcNow,
    };

    db.Orders.Add(order);
    db.Set<OutboxMessage>().Add(OutboxMessage.From(
        new OrderCreated(order.Id, order.Amount, order.CustomerEmail),
        aggregateKey: order.Id.ToString()));

    await db.SaveChangesAsync(); // одна транзакция на обе строки

    return Results.Accepted($"/orders/{order.Id}", new { order.Id, order.Status });
});

app.MapPrometheusScrapingEndpoint(); // GET /metrics для Prometheus

app.MapGet("/orders/{id:guid}", async (Guid id, OrdersDbContext db) =>
    await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id) is { } order
        ? Results.Ok(new { order.Id, order.Status, order.Amount, order.CustomerEmail })
        : Results.NotFound());

app.Run("http://localhost:5300");

public sealed record CreateOrderRequest(string CustomerEmail, decimal Amount);
