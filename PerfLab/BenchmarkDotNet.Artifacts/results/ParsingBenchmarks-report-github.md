```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-13500H 2.60GHz, 1 CPU, 16 logical and 12 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method     | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|----------- |---------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| SplitParse | 77.68 μs | 36.42 μs | 1.996 μs |  1.00 |    0.03 | 17.8223 |  167928 B |        1.00 |
| SpanParse  | 48.41 μs | 21.17 μs | 1.161 μs |  0.62 |    0.02 |       - |         - |        0.00 |
