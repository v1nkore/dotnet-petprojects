using System.Collections.Concurrent;
using CleanLab.Application;
using CleanLab.Domain;

namespace CleanLab.Infrastructure;

/// <summary>
/// ИНФРАСТРУКТУРА: реализация портов application-слоя. In-memory нарочно:
/// вся ценность архитектуры в том, что замена этого класса на EF/Dapper
/// не тронет ни Domain, ни Application — зависимости смотрят внутрь.
/// Один класс реализует оба порта: команды и запросы могут ходить
/// в одно хранилище — CQRS не обязан означать две базы.
/// </summary>
public sealed class InMemoryDebtStore : IDebtRepository, IDebtReadStore
{
    private readonly ConcurrentDictionary<Guid, Debt> _debts = new();

    public Task<Debt?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_debts.GetValueOrDefault(id));

    public Task AddAsync(Debt debt, CancellationToken ct)
    {
        _debts[debt.Id] = debt;
        return Task.CompletedTask;
    }

    public Task SaveAsync(Debt debt, CancellationToken ct) => Task.CompletedTask; // ссылка уже в словаре

    public Task<DebtDto?> FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_debts.TryGetValue(id, out var d)
            ? new DebtDto(d.Id, d.ContractNumber, d.Principal.Amount, d.Paid.Amount, d.Outstanding.Amount, d.IsClosed)
            : null);
}
