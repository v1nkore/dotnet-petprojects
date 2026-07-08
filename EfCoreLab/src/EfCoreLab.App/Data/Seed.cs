using Microsoft.EntityFrameworkCore;

namespace EfCoreLab.App.Data;

public static class Seed
{
    public const int Debtors = 100;
    public const int DebtsPerDebtor = 3;
    public const int Payments = 300_000;

    public static async Task EnsureSeededAsync(CollectionsDbContext db)
    {
        await using (db)
        {
            await db.Database.EnsureCreatedAsync();

            if (await db.Debtors.AnyAsync())
            {
                Console.WriteLine("База уже насеяна — пропускаем.");
                return;
            }

            var rng = new Random(42);

            // Должники и долги — через EF: объёмы маленькие, удобно
            var debtors = Enumerable.Range(1, Debtors)
                .Select(i => new Debtor
                {
                    Name = $"Должник №{i}",
                    Debts = Enumerable.Range(1, DebtsPerDebtor)
                        .Select(_ => new Debt
                        {
                            Principal = rng.Next(10_000, 500_000),
                            Status = DebtStatus.Active,
                        })
                        .ToList(),
                })
                .ToList();

            db.Debtors.AddRange(debtors);
            await db.SaveChangesAsync();

            // 300k платежей — НЕ через EF: AddRange трекает каждую сущность
            // (снапшоты, identity map) и вставляет построчно. generate_series
            // делает это одним оператором прямо в базе за доли секунды.
            // Урок: EF — не инструмент для bulk-вставок.
            Console.WriteLine($"Сеем {Payments:N0} платежей через generate_series...");
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "Payments" ("DebtId", "Amount", "PaidAt", "Status")
                SELECT
                    (random() * {Debtors * DebtsPerDebtor - 1})::int + 1,
                    round((random() * 9900 + 100)::numeric, 2),
                    timestamp '2025-01-01' + random() * interval '365 days',
                    (random() * 2)::int
                FROM generate_series(1, {Payments})
                """);

            Console.WriteLine("База насеяна.");
        }
    }
}
