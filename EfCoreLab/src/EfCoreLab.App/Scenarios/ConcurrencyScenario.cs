using EfCoreLab.App.Data;
using Microsoft.EntityFrameworkCore;

namespace EfCoreLab.App.Scenarios;

/// <summary>
/// Optimistic concurrency через xmin (аналог rowversion в Postgres).
/// Два «оператора» открыли одну карточку долга; оба сохраняют.
/// Без токена второй молча затёр бы первого (lost update).
/// С токеном — DbUpdateConcurrencyException, и конфликт решается осознанно.
/// </summary>
public static class ConcurrencyScenario
{
    public static async Task RunAsync(Func<bool, CollectionsDbContext> createContext)
    {
        Console.WriteLine("\n═══ Сценарий 3: optimistic concurrency (xmin) ═══\n");

        await using var dbOperator1 = createContext(false);
        await using var dbOperator2 = createContext(false);

        // Оба читают ОДНУ строку — у каждого свой снапшот с одинаковым xmin
        var debtFor1 = await dbOperator1.Debts.OrderBy(d => d.Id).FirstAsync();
        var debtFor2 = await dbOperator2.Debts.OrderBy(d => d.Id).FirstAsync();
        Console.WriteLine($"Оба оператора открыли долг #{debtFor1.Id}, xmin = {debtFor1.Version}");

        // Оператор 1 успевает первым. Toggle, а не присваивание константы:
        // при повторном запуске сценария статус уже Restructured, и присваивание
        // того же значения не меняет ничего → SaveChanges не отправит UPDATE вовсе
        // (change tracker сравнил снапшоты), xmin не изменится и конфликта не будет.
        // Ещё один урок про change tracking, найденный этим же сценарием.
        debtFor1.Status = debtFor1.Status == DebtStatus.Active
            ? DebtStatus.Restructured
            : DebtStatus.Active;
        await dbOperator1.SaveChangesAsync();
        Console.WriteLine($"Оператор 1 сохранил Status={debtFor1.Status}, новый xmin = {debtFor1.Version}");

        // Оператор 2 правит сумму, не зная об изменении.
        // UPDATE ... WHERE id = @id AND xmin = @старый_xmin → 0 строк → исключение
        debtFor2.Principal += 1000m;
        try
        {
            await dbOperator2.SaveChangesAsync();
            Console.WriteLine("!!! Lost update — токен не сработал (так быть не должно)");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Console.WriteLine("Оператор 2 получил DbUpdateConcurrencyException — lost update предотвращён.");

            // Стратегия «перечитать и повторить»: берём свежие значения БД за основу,
            // накатываем свою правку заново
            var entry = ex.Entries.Single();
            var fresh = await entry.GetDatabaseValuesAsync()
                ?? throw new InvalidOperationException("Строка удалена конкурентно");
            entry.OriginalValues.SetValues(fresh); // обновляем и снапшот, и токен
            entry.CurrentValues.SetValues(fresh);
            ((Debt)entry.Entity).Principal += 1000m;

            await dbOperator2.SaveChangesAsync();
            Console.WriteLine($"Retry успешен: Status={((Debt)entry.Entity).Status} (правка оператора 1 сохранена), Principal увеличен.");
        }

        Console.WriteLine("""

        Вывод: xmin даёт optimistic concurrency бесплатно — без своей колонки и миграции.
        Обработка конфликта — всегда осознанное решение: retry, merge или отдать 409 пользователю.
        """);
    }
}
