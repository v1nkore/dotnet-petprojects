using BenchmarkDotNet.Running;
using TtlCache.Benchmarks;

// Запуск строго в Release: dotnet run -c Release --project benchmarks/TtlCache.Benchmarks
// Быстрая прикидка (~2 мин вместо ~15): добавить аргумент --job short
BenchmarkRunner.Run<CacheBenchmarks>(args: args);
