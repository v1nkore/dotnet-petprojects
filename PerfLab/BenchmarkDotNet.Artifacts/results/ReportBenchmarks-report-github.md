```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-13500H 2.60GHz, 1 CPU, 16 logical and 12 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method              | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|-------------------- |----------:|-----------:|----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| StringConcat        | 318.16 μs | 636.393 μs | 34.883 μs |  1.01 |    0.14 | 674.3164 | 41.9922 | 6204.04 KB |       1.000 |
| StringBuilderReport |  16.17 μs |   6.324 μs |  0.347 μs |  0.05 |    0.01 |   5.8899 |  0.3052 |   54.35 KB |       0.009 |
