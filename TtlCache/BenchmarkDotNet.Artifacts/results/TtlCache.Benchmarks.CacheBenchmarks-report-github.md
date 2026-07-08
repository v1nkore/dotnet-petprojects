```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-13500H 2.60GHz, 1 CPU, 16 logical and 12 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method               | Threads | Mean         | Error        | StdDev       | Ratio | RatioSD | Completed Work Items | Lock Contentions | Allocated | Alloc Ratio |
|--------------------- |-------- |-------------:|-------------:|-------------:|------:|--------:|---------------------:|-----------------:|----------:|------------:|
| **LockDictionary**       | **1**       |   **3,063.0 μs** |     **465.8 μs** |     **25.53 μs** |  **1.00** |    **0.01** |                    **-** |                **-** |      **24 B** |        **1.00** |
| ConcurrentDictionary | 1       |     810.0 μs |     225.6 μs |     12.36 μs |  0.26 |    0.00 |                    - |                - |      24 B |        1.00 |
| ReaderWriterLockSlim | 1       |   3,324.3 μs |   5,546.1 μs |    304.00 μs |  1.09 |    0.09 |                    - |                - |      24 B |        1.00 |
|                      |         |              |              |              |       |         |                      |                  |           |             |
| **LockDictionary**       | **4**       |  **39,170.5 μs** |  **26,764.2 μs** |  **1,467.04 μs** |  **1.00** |    **0.05** |               **4.0000** |          **65.0000** |     **872 B** |        **1.00** |
| ConcurrentDictionary | 4       |   1,790.8 μs |     220.1 μs |     12.07 μs |  0.05 |    0.00 |               4.0000 |           0.1250 |     872 B |        1.00 |
| ReaderWriterLockSlim | 4       |  19,741.1 μs |  47,626.3 μs |  2,610.56 μs |  0.50 |    0.06 |               4.0000 |                - |     872 B |        1.00 |
|                      |         |              |              |              |       |         |                      |                  |           |             |
| **LockDictionary**       | **8**       | **192,820.5 μs** | **526,207.2 μs** | **28,843.19 μs** |  **1.01** |    **0.18** |               **8.0000** |        **1571.0000** |    **1576 B** |        **1.00** |
| ConcurrentDictionary | 8       |   3,025.6 μs |     300.4 μs |     16.47 μs |  0.02 |    0.00 |               8.0000 |           0.7383 |    1576 B |        1.00 |
| ReaderWriterLockSlim | 8       |  79,902.0 μs |  78,612.1 μs |  4,309.00 μs |  0.42 |    0.05 |               8.0000 |                - |    1576 B |        1.00 |
