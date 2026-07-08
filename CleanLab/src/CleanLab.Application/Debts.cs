using CleanLab.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanLab.Application;

// APPLICATION-СЛОЙ: сценарии использования. Оркестрирует домен, сам правил не содержит.
// Зависит только от Domain. Порты (интерфейсы) объявлены ЗДЕСЬ, реализации — снаружи,
// в Infrastructure: это и есть «развернуть зависимости внутрь» (Dependency Inversion).

public interface IDebtRepository
{
    Task<Debt?> GetAsync(Guid id, CancellationToken ct);
    Task AddAsync(Debt debt, CancellationToken ct);
    Task SaveAsync(Debt debt, CancellationToken ct);
}

/// <summary>Читающая сторона CQRS: своя модель (DTO), минуя агрегат и его инварианты.</summary>
public interface IDebtReadStore
{
    Task<DebtDto?> FindAsync(Guid id, CancellationToken ct);
}

public sealed record DebtDto(Guid Id, string ContractNumber, decimal Principal, decimal Paid, decimal Outstanding, bool IsClosed);

// === Команды: меняют состояние, возвращают минимум ===

public sealed record OpenDebtCommand(string ContractNumber, decimal Amount) : IRequest<Guid>;

public sealed class OpenDebtHandler(IDebtRepository repository, IPublisher publisher) : IRequestHandler<OpenDebtCommand, Guid>
{
    public async Task<Guid> Handle(OpenDebtCommand command, CancellationToken ct)
    {
        var debt = Debt.Open(command.ContractNumber, Money.Rub(command.Amount));
        await repository.AddAsync(debt, ct);
        await PublishEventsAsync(publisher, debt, ct);
        return debt.Id;
    }

    /// <summary>
    /// Доменные события публикуются ПОСЛЕ сохранения: подписчики не должны
    /// реагировать на то, чего в базе (ещё) нет. В проде на этом месте — Outbox (см. OrderFlow).
    /// </summary>
    internal static async Task PublishEventsAsync(IPublisher publisher, Debt debt, CancellationToken ct)
    {
        foreach (var domainEvent in debt.DequeueEvents())
            await publisher.Publish(new DomainEventNotification(domainEvent), ct);
    }
}

public sealed record RegisterPaymentCommand(Guid DebtId, decimal Amount) : IRequest;

public sealed class RegisterPaymentHandler(IDebtRepository repository, IPublisher publisher) : IRequestHandler<RegisterPaymentCommand>
{
    public async Task Handle(RegisterPaymentCommand command, CancellationToken ct)
    {
        var debt = await repository.GetAsync(command.DebtId, ct)
                   ?? throw new DomainException("Долг не найден");

        debt.RegisterPayment(Money.Rub(command.Amount)); // вся логика — в агрегате

        await repository.SaveAsync(debt, ct);
        await OpenDebtHandler.PublishEventsAsync(publisher, debt, ct);
    }
}

// === Запрос: не меняет состояние, читает напрямую в DTO ===

public sealed record GetDebtQuery(Guid DebtId) : IRequest<DebtDto?>;

public sealed class GetDebtHandler(IDebtReadStore readStore) : IRequestHandler<GetDebtQuery, DebtDto?>
{
    public Task<DebtDto?> Handle(GetDebtQuery query, CancellationToken ct) =>
        readStore.FindAsync(query.DebtId, ct);
}

// === Доменные события → реакции ===

/// <summary>Мост: доменное событие (чистый Domain) → MediatR-уведомление (Application).</summary>
public sealed record DomainEventNotification(IDomainEvent DomainEvent) : INotification;

/// <summary>Побочный эффект живёт в подписчике, а не в агрегате: агрегат фиксирует факт, реакция — здесь.</summary>
public sealed class DebtClosedHandler(ILogger<DebtClosedHandler> logger) : INotificationHandler<DomainEventNotification>
{
    public Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        if (notification.DomainEvent is DebtClosed closed)
            logger.LogInformation("Долг {DebtId} закрыт — отправляем должнику SMS-поздравление", closed.DebtId);
        return Task.CompletedTask;
    }
}

// === Cross-cutting: pipeline behavior (аналог middleware для команд/запросов) ===

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        logger.LogInformation("→ {Request}", typeof(TRequest).Name);
        var response = await next(ct);
        logger.LogInformation("← {Request} OK", typeof(TRequest).Name);
        return response;
    }
}
