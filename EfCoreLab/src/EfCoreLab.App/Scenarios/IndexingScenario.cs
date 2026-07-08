using System.Diagnostics;
using EfCoreLab.App.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EfCoreLab.App.Scenarios;

/// <summary>
/// EXPLAIN ANALYZE до и после индекса — самый убедительный аргумент в пользу
/// «читайте план запроса»: Seq Scan по сотням тысяч строк превращается
/// в Index Scan, и это видно в самом плане и в цифрах execution time.
/// </summary>
public static class IndexingScenario
{
    private const string Query = """
        SELECT "DebtId", SUM("Amount")
        FROM "Payments"
        WHERE "Status" = 1 AND "PaidAt" >= '2025-06-01' AND "PaidAt" < '2025-07-01'
        GROUP BY "DebtId"
        """;

    public static async Task RunAsync(string connectionString)
    {
        Console.WriteLine("\n═══ Сценарий 4: EXPLAIN ANALYZE до и после индекса ═══\n");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await Drop(conn); // чистый старт при повторных запусках

        Console.WriteLine("--- БЕЗ индекса ---");
        var before = await Explain(conn);

        Console.WriteLine("\nСоздаём составной покрывающий индекс:");
        const string createIndex = """
            CREATE INDEX ix_payments_status_paidat
            ON "Payments" ("Status", "PaidAt")
            INCLUDE ("DebtId", "Amount")
            """;
        Console.WriteLine(createIndex + "\n");
        await Exec(conn, createIndex);
        // VACUUM, а не просто ANALYZE: без заполненной visibility map Postgres
        // не может делать Index Only Scan даже по покрывающему индексу —
        // ему пришлось бы ходить в heap проверять видимость версий строк (MVCC)
        await Exec(conn, "VACUUM ANALYZE \"Payments\"");

        Console.WriteLine("--- С индексом ---");
        var after = await Explain(conn);

        Console.WriteLine($"""

        Вывод: {before:F1} мс → {after:F1} мс.
        Смотри на узлы плана: Parallel Seq Scan (перебор всей таблицы, Rows Removed by Filter —
        впустую прочитанные строки) сменился скана́ми по индексу.
        Порядок колонок в индексе не случаен: равенство (Status) → диапазон (PaidAt).
        INCLUDE делает индекс покрывающим: Index Only Scan возможен, только если visibility map
        актуальна (поэтому VACUUM, а не просто ANALYZE) — иначе планировщик выберет Bitmap Scan.
        """);
    }

    private static async Task<double> Explain(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS) " + Query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        double executionMs = 0;
        while (await reader.ReadAsync())
        {
            var line = reader.GetString(0);
            // Печатаем только смысловые строки плана — узлы и итог
            if (line.Contains("Scan") || line.Contains("Aggregate")
                || line.Contains("Execution Time") || line.Contains("Rows Removed")
                || line.Contains("Buffers"))
                Console.WriteLine("  " + line.Trim());

            if (line.StartsWith("Execution Time:"))
                executionMs = double.Parse(line.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);
        }

        return executionMs;
    }

    private static Task Drop(NpgsqlConnection conn) =>
        Exec(conn, "DROP INDEX IF EXISTS ix_payments_status_paidat");

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
