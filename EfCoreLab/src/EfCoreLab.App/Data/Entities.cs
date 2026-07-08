namespace EfCoreLab.App.Data;

// Домен — взыскание: должник → долги → платежи.
// Навигации virtual — обязательное условие для lazy-loading proxies
// (прокси наследует класс и переопределяет свойство, подкладывая запрос к БД).

public class Debtor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public virtual ICollection<Debt> Debts { get; set; } = [];
}

public enum DebtStatus
{
    Active,
    Restructured,
    Closed,
}

public class Debt
{
    public int Id { get; set; }
    public int DebtorId { get; set; }
    public virtual Debtor Debtor { get; set; } = null!;
    public decimal Principal { get; set; }
    public DebtStatus Status { get; set; }

    // Токен оптимистичной конкуренции. В SQL Server это rowversion,
    // в Postgres — системная колонка xmin (id транзакции, менявшей строку последней):
    // отдельного поля в таблице НЕТ, Npgsql маппит uint-свойство прямо на неё.
    public uint Version { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = [];
}

public enum PaymentStatus
{
    Pending,
    Confirmed,
    Reversed,
}

public class Payment
{
    public long Id { get; set; }
    public int DebtId { get; set; }
    public virtual Debt Debt { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public PaymentStatus Status { get; set; }
}
