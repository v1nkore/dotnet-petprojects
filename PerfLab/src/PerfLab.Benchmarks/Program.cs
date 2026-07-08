using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run(typeof(Program).Assembly, args: args);

/// <summary>
/// История «горячий метод → убрать аллокации»: парсинг строк реестра платежей
/// формата "id;сумма;дата". Naive-версия аллоцирует массив + 3 строки на строку
/// реестра; Span-версия — ноль аллокаций: срезы смотрят в исходную строку.
/// </summary>
[MemoryDiagnoser]
public class ParsingBenchmarks
{
    private string[] _lines = [];

    [GlobalSetup]
    public void Setup() =>
        _lines = Enumerable.Range(1, 1000)
            .Select(i => $"{i};{i * 137.5m:F2};2026-07-{i % 28 + 1:D2}")
            .ToArray();

    [Benchmark(Baseline = true)]
    public decimal SplitParse()
    {
        decimal sum = 0;
        foreach (var line in _lines)
        {
            var parts = line.Split(';');          // массив + 3 новых строки на каждую строку
            sum += decimal.Parse(parts[1]);
        }
        return sum;
    }

    [Benchmark]
    public decimal SpanParse()
    {
        decimal sum = 0;
        foreach (var line in _lines)
        {
            var span = line.AsSpan();             // «окно» в строку, не копия
            var afterId = span[(span.IndexOf(';') + 1)..];
            var amount = afterId[..afterId.IndexOf(';')];
            sum += decimal.Parse(amount);         // Parse умеет ReadOnlySpan<char> — строка не создаётся
        }
        return sum;
    }
}

/// <summary>
/// Вторая классика: построение текстового отчёта. Конкатенация в цикле —
/// O(n²) по копированию и горы мусора в Gen0; StringBuilder — линейно.
/// </summary>
[MemoryDiagnoser]
public class ReportBenchmarks
{
    private const int Rows = 500;

    [Benchmark(Baseline = true)]
    public string StringConcat()
    {
        var report = "";
        for (var i = 0; i < Rows; i++)
            report += $"Платёж {i}: {i * 100.5m:N2} RUB\n"; // каждая += копирует ВСЁ накопленное
        return report;
    }

    [Benchmark]
    public string StringBuilderReport()
    {
        var sb = new StringBuilder(capacity: Rows * 32);   // ёмкость сразу — без перевыделений
        for (var i = 0; i < Rows; i++)
            sb.Append("Платёж ").Append(i).Append(": ").Append(i * 100.5m).Append(" RUB\n");
        return sb.ToString();
    }
}
