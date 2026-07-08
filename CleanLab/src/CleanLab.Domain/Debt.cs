namespace CleanLab.Domain;

// ДОМЕННЫЙ СЛОЙ — центр луковицы. Не знает НИ О ЧЁМ снаружи:
// ни EF, ни MediatR, ни HTTP. Только бизнес-правила. Единственный слой,
// который переживёт смену базы, фреймворка и транспорта.

/// <summary>
/// Value object: без идентичности, равенство по значению, иммутабельный.
/// Инкапсулирует инвариант «нельзя сложить рубли с долларами» —
/// вместо голого decimal, который позволяет любую бессмыслицу.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Rub(decimal amount) => new(amount, "RUB");

    public static Money operator +(Money a, Money b) => a.With(b, (x, y) => x + y);
    public static Money operator -(Money a, Money b) => a.With(b, (x, y) => x - y);

    private Money With(Money other, Func<decimal, decimal, decimal> op) =>
        Currency == other.Currency
            ? this with { Amount = op(Amount, other.Amount) }
            : throw new DomainException($"Разные валюты: {Currency} и {other.Currency}");

    public override string ToString() => $"{Amount:N2} {Currency}";
}

/// <summary>Маркер доменного события: факт, случившийся в домене, в прошедшем времени.</summary>
public interface IDomainEvent;

public sealed record DebtOpened(Guid DebtId, Money Principal) : IDomainEvent;
public sealed record DebtClosed(Guid DebtId) : IDomainEvent;

/// <summary>
/// Агрегат: граница транзакционной согласованности. Все инварианты долга
/// защищены здесь — снаружи НЕВОЗМОЖНО получить некорректное состояние:
/// сеттеры приватные, изменения только через методы с проверками.
/// </summary>
public sealed class Debt
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; }
    public string ContractNumber { get; }
    public Money Principal { get; }
    public Money Paid { get; private set; }
    public bool IsClosed { get; private set; }

    public Money Outstanding => Principal - Paid;

    private Debt(Guid id, string contractNumber, Money principal)
    {
        Id = id;
        ContractNumber = contractNumber;
        Principal = principal;
        Paid = principal with { Amount = 0 };
    }

    /// <summary>Фабрика вместо конструктора: валидация + событие в одном месте.</summary>
    public static Debt Open(string contractNumber, Money principal)
    {
        if (string.IsNullOrWhiteSpace(contractNumber))
            throw new DomainException("Номер договора обязателен");
        if (principal.Amount <= 0)
            throw new DomainException("Сумма долга должна быть положительной");

        var debt = new Debt(Guid.NewGuid(), contractNumber, principal);
        debt._events.Add(new DebtOpened(debt.Id, principal));
        return debt;
    }

    public void RegisterPayment(Money payment)
    {
        if (IsClosed)
            throw new DomainException("Долг уже закрыт");
        if (payment.Amount <= 0)
            throw new DomainException("Платёж должен быть положительным");
        if (payment.Amount > Outstanding.Amount)
            throw new DomainException($"Платёж {payment} больше остатка {Outstanding}");

        Paid += payment;

        if (Outstanding.Amount == 0)
        {
            IsClosed = true;
            _events.Add(new DebtClosed(Id)); // не отправляем SMS здесь — фиксируем ФАКТ
        }
    }

    /// <summary>События забираются один раз — публикует их application-слой после сохранения.</summary>
    public IReadOnlyList<IDomainEvent> DequeueEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }
}

public sealed class DomainException(string message) : Exception(message);
