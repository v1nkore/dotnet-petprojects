# EfCoreLab — EF Core и базы данных на реальном Postgres

Pet-проект №6 из плана подготовки. Закрывает разделы 4–5 из «200 ответов»:
**EF Core** (N+1, change tracking, AsNoTracking, optimistic concurrency)
и **Базы данных и SQL** (составные/покрывающие индексы, чтение плана, MVCC/VACUUM).

## Предметная область

Домен взыскания (знакомый по работе): `Debtor → Debt → Payment`, 300 000 платежей,
насеянных `generate_series` (см. урок про bulk-вставки в `Data/Seed.cs`).
Консольное приложение прогоняет 4 сценария подряд и печатает доказательства цифрами.

## Как запустить

```bash
docker compose up -d          # Postgres 16 на порту 5455
dotnet run --project src/EfCoreLab.App -c Release
```

Сценарии идемпотентны — можно запускать сколько угодно раз.

## Сценарии и результаты с этой машины

### 1. N+1 (`Scenarios/NPlusOneScenario.cs`)
Один и тот же отчёт «должники и суммы долгов» тремя способами. Число запросов считает
`QueryCountingInterceptor` (DbCommandInterceptor) — N+1 ловится числом, а не ощущением:

| Способ | SQL-запросов | Время |
|---|---:|---:|
| lazy loading (proxies) | **101** | 306 мс |
| `Include` + `AsNoTracking` | 1 | 50 мс |
| проекция (`Select` с `Sum` в базе) | 1 | 69 мс |

### 2. Change tracking (`Scenarios/ChangeTrackingScenario.cs`)
50 000 строк: с трекингом 313 мс и 50 000 сущностей в change tracker'е,
с `AsNoTracking` — 53 мс и 0. Плюс демонстрация identity map.

### 3. Optimistic concurrency (`Scenarios/ConcurrencyScenario.cs`)
Токен — системная колонка Postgres **xmin** (аналог rowversion, но без своей колонки:
`uint Version` + `IsRowVersion()` у Npgsql). Два «оператора» правят один долг →
второй получает `DbUpdateConcurrencyException` → retry по свежим значениям БД.

### 4. Индексы и план (`Scenarios/IndexingScenario.cs`)
`EXPLAIN (ANALYZE, BUFFERS)` до и после составного покрывающего индекса
`(Status, PaidAt) INCLUDE (DebtId, Amount)`:

| | План | Время | Буферы |
|---|---|---:|---:|
| до | Parallel Seq Scan, 143 918 строк отброшено фильтром | 12.1 мс | 2507 |
| после | **Index Only Scan** | 2.4 мс | 77 |

## Три пойманных нюанса (готовые истории на собес)

1. **Присваивание того же значения ≠ изменение.** Со второго запуска сценарий
   конкуренции «ломался»: `Status = Restructured` при уже `Restructured` → change tracker
   не видит diff → UPDATE не отправляется → xmin не меняется → конфликта нет.
2. **Index Only Scan требует VACUUM.** После `CREATE INDEX` + `ANALYZE` планировщик
   выбирал Bitmap Heap Scan: без актуальной visibility map Postgres обязан ходить в heap
   проверять видимость версий строк (MVCC). `VACUUM ANALYZE` — и появился Index Only Scan.
3. **EF — не инструмент bulk-вставки.** 300k строк через `AddRange` — это 300k снапшотов
   в change tracker'е; `generate_series` одним оператором — доли секунды.

Подробный план изучения — в **study-guide.html**.
