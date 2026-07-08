// «Банк» — намеренно нестабильный внешний сервис, каким он бывает в жизни.
// Его нестабильность — то, ради чего в Payments стоит Polly:
//   ~35% запросов → 500 (transient, лечится ретраем)
//   amount >= 5000 → 402 (бизнес-отказ: ретраить БЕССМЫСЛЕННО — важное различие)
//   случайная задержка 20–150 мс, изредка — «зависание» на 5 секунд (ловится таймаутом)

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("bank"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")));

var app = builder.Build();

var rng = new Random();

app.MapPost("/charge", async (ChargeRequest request, ILogger<Program> logger) =>
{
    // Изредка притворяемся зависшим — per-attempt timeout в Payments отработает раньше
    if (rng.NextDouble() < 0.07)
    {
        logger.LogWarning("Charge {OrderId}: имитируем зависание", request.OrderId);
        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    await Task.Delay(rng.Next(20, 150)); // обычная сетевая задержка

    if (request.Amount >= 5000m)
    {
        logger.LogWarning("Charge {OrderId}: отказ — лимит", request.OrderId);
        return Results.Json(new { reason = "Превышен лимит на операцию" }, statusCode: 402);
    }

    if (rng.NextDouble() < 0.35)
    {
        logger.LogWarning("Charge {OrderId}: 500 (transient)", request.OrderId);
        return Results.StatusCode(500);
    }

    var transactionId = $"tx-{Guid.NewGuid():N}";
    logger.LogInformation("Charge {OrderId}: OK {TransactionId}", request.OrderId, transactionId);
    return Results.Ok(new ChargeResponse(transactionId));
});

app.Run("http://localhost:5301");

public sealed record ChargeRequest(Guid OrderId, decimal Amount);
public sealed record ChargeResponse(string TransactionId);
