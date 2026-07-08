using CleanLab.Application;
using CleanLab.Domain;
using CleanLab.Infrastructure;
using MediatR;

// API-СЛОЙ: тонкий транспорт. Принял HTTP → отправил команду/запрос → вернул результат.
// Никакой логики: endpoint можно заменить на gRPC/консоль, не трогая остальное.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<OpenDebtCommand>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});

var store = new InMemoryDebtStore();
builder.Services.AddSingleton<IDebtRepository>(store);
builder.Services.AddSingleton<IDebtReadStore>(store);

var app = builder.Build();

app.MapPost("/debts", async (OpenDebtRequest request, IMediator mediator) =>
{
    var id = await mediator.Send(new OpenDebtCommand(request.ContractNumber, request.Amount));
    return Results.Created($"/debts/{id}", new { id });
});

app.MapPost("/debts/{id:guid}/payments", async (Guid id, PaymentRequest request, IMediator mediator) =>
{
    await mediator.Send(new RegisterPaymentCommand(id, request.Amount));
    return Results.NoContent();
});

app.MapGet("/debts/{id:guid}", async (Guid id, IMediator mediator) =>
    await mediator.Send(new GetDebtQuery(id)) is { } dto ? Results.Ok(dto) : Results.NotFound());

// Доменные исключения → 422: маппинг ошибок — забота транспортного слоя
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (DomainException ex)
    {
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.Run("http://localhost:5400");

public sealed record OpenDebtRequest(string ContractNumber, decimal Amount);
public sealed record PaymentRequest(decimal Amount);
