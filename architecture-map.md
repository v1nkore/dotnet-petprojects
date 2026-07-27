# Карта архитектурных паттернов PetProjects

Что и где смотреть: паттерн → проект → файлы/классы. Пути от `PetProjects/`.

## 1. Clean Architecture / Onion — CleanLab

Направление зависимостей `Api → Infrastructure → Application → Domain`, всё смотрит внутрь.

- `CleanLab/src/CleanLab.Domain/Debt.cs` — домен, не зависит ни от чего (ни EF, ни MediatR, ни HTTP).
- `CleanLab/src/CleanLab.Application/Debts.cs` — сценарии + порты (`IDebtRepository`, `IDebtReadStore`).
- `CleanLab/src/CleanLab.Infrastructure/InMemoryDebtStore.cs` — реализация портов (заменяемо на EF без правок Domain/Application).
- `CleanLab/src/CleanLab.Api/Program.cs` — тонкий транспорт: HTTP → `mediator.Send`, никакой логики.
- Диаграмма зависимостей — `CleanLab/README.md`; «DIP = вся суть Clean Architecture» — `SolidMap/README.md`.

## 2. DDD-тактика — CleanLab (`Domain/Debt.cs`)

- **Value Object:** `Money` (readonly record struct) — инвариант «нельзя сложить RUB и USD» в операторах.
- **Агрегат:** `Debt` — приватные сеттеры, некорректное состояние недостижимо снаружи.
- **Фабрика:** `Debt.Open(...)` — валидация + событие в одной точке, ctor приватный.
- **Инварианты:** `RegisterPayment(...)` — «долг закрыт», «платёж больше остатка».
- **Доменные события:** `IDomainEvent`, `DebtOpened`/`DebtClosed`, буфер `_events` + `DequeueEvents()`; побочный эффект — в подписчике application-слоя (`DebtClosedHandler`), не в агрегате.

Смежно: интеграционные события как версионируемый контракт — `OrderFlow/src/OrderFlow.Shared/Contracts/Events.cs`.

## 3. CQRS — CleanLab (`Application/Debts.cs`)

- Команды через агрегат: `OpenDebtCommand/Handler`, `RegisterPaymentCommand/Handler`.
- Запрос мимо инвариантов сразу в DTO: `GetDebtQuery/Handler` → `DebtDto`.
- Раздельные порты записи/чтения, но одна реализация `InMemoryDebtStore` — «CQRS ≠ две базы».

## 4. MediatR pipeline behaviors — CleanLab

- `LoggingBehavior<TRequest,TResponse> : IPipelineBehavior<,>` (в `Debts.cs`), регистрация `AddOpenBehavior` в `Api/Program.cs`.
- Мост домен → MediatR: `DomainEventNotification : INotification` + `DebtClosedHandler`.

## 5. Repository / Unit of Work

- Порты-репозитории: CleanLab (`IDebtRepository`/`IDebtReadStore`).
- DbContext как UoW: `OrderFlow/src/OrderFlow.Orders/Data/OrdersDbContext.cs`, `PaymentsDbContext` — бизнес-запись + outbox + отметка идемпотентности одним `SaveChangesAsync`; `EfCoreLab/src/EfCoreLab.App/Data/CollectionsDbContext.cs`.
- «DbContext уже UoW, DbSet уже репозиторий» — `SolidMap/README.md`.

## 6. Transactional Outbox — OrderFlow

- Модель: `OrderFlow/src/OrderFlow.Shared/Outbox/OutboxEntities.cs` — `OutboxMessage` (+ `From<T>`, хранит `TraceParent`), частичный индекс; `OutboxModelBuilder.AddOutboxEntities()`.
- Запись в одной транзакции: `Orders/Program.cs` (`POST /orders`), `Payments/OrderEventsConsumer.cs`.
- Публикация: `Shared/Outbox/OutboxPublisher.cs` — `OutboxPublisher<TContext> : BackgroundService`, PeriodicTimer → Kafka → `PublishedAtUtc`. At-least-once.
- Продюсер: `KafkaSetup.CreateProducer` — `Acks.All` + `EnableIdempotence`.

## 7. Idempotent Consumer (Inbox) — OrderFlow

- `ProcessedMessage` (PK = `MessageId`) в `OutboxEntities.cs`.
- `Payments/OrderEventsConsumer.cs` — проверка ДО побочных эффектов (второе списание — худший баг); эффект + отметка одной транзакцией.
- `Orders/Consumers/PaymentEventsConsumer.cs` — та же дедупликация. `MessageId` живёт в `EventEnvelope` (`Contracts/Events.cs`).

## 8. Saga (хореография) + компенсации — OrderFlow

- Шаг 2: `Payments/OrderEventsConsumer.cs` — слушает `OrderCreated` → `PaymentSucceeded`/`PaymentFailed`.
- Замыкание: `Orders/Consumers/PaymentEventsConsumer.cs` — `Succeeded → Completed`, `Failed → Cancelled` (компенсирующая транзакция, не распределённый rollback).
- Оркестраторная сага — только теория: `SystemDesign/01-payment-gateway.md`.

## 9. Event-driven / pub-sub (Kafka) — OrderFlow

- Каркас консюмера: `Shared/Kafka/KafkaConsumerService.cs` — abstract BackgroundService, LongRunning-поток, ручной коммит после обработки (`EnableAutoCommit=false`), `Close()` при остановке. Template Method: наследник реализует только `HandleAsync`.
- Ключ сообщения = `OrderId` → порядок событий заказа внутри партиции (`Contracts/Events.cs`, `KafkaTopics`).

## 10. Retry / Circuit Breaker / Timeout (Polly v8) — OrderFlow

- `Payments/Program.cs` — `AddResilienceHandler("bank")`: total timeout 10 с → retry ×4 (экспонента + jitter) → circuit breaker (0.9 / throughput 10 / break 5 с) → per-attempt timeout 1 с. Порядок = семантика.
- `Payments/BankClient.cs` — типизированный клиент, о политике не знает; 402 = бизнес-отказ (не ретраится), 5xx = transient.
- `BrokenCircuitException` ловится в `OrderEventsConsumer.cs`. Нестабильный банк: `Bank/Program.cs` (~35% 500).

## 11. Кэш-паттерны — RedisLab, TtlCache

`RedisLab/src/RedisLab.App/Scenarios.cs`:
- **Cache-aside:** `GetCacheAsideAsync` — miss → БД → SET с TTL одной командой.
- **Stampede-защита:** `GetSingleFlightAsync` (SemaphoreSlim + double-check, in-process) и `GetWithDistributedLockAsync` (SET NX PX + токен + Lua compare-and-delete).
- **Инвалидация:** `InvalidationAsync` — «обновить БД + DEL» (не перезапись), TTL как страховка.
- `Program.cs` — один `ConnectionMultiplexer` на приложение (как HttpClient).

`TtlCache/src/TtlCache.Core/TtlCache.cs` — in-memory TTL-кэш: ленивое + активное вытеснение (свипер на PeriodicTimer), `GetOrAdd`, hit/miss-статистика.

## 12. Producer/Consumer (Channels) + воркеры — AspNetLab

- `Notifications/NotificationQueue.cs` — `Channel.CreateBounded` (100, `FullMode.Wait` = backpressure, `SingleReader`).
- `Notifications/NotificationDispatcher.cs` — BackgroundService: `await foreach ReadAllAsync` (push, не полинг), catch внутри цикла (иначе исключение гасит хост), scope на единицу работы.
- `Controllers/NotificationsController.cs` — продюсер, `202 Accepted` + Location.

## 13. DI-паттерны — AspNetLab

- **Options:** `Notifications/DispatcherOptions.cs` + `BindConfiguration().ValidateDataAnnotations().ValidateOnStart()` в `Program.cs`; потребитель `FakeEmailSender` через `IOptions<>`.
- **Captive dependency (намеренный анти-пример):** `DiTraps/CaptiveTrap.cs` — singleton со scoped-зависимостью; ловится `ValidateOnBuild` (флаг `--BreakDi=true`).
- **Scope-per-unit-of-work:** `NotificationDispatcher.cs` (`IServiceScopeFactory.CreateAsyncScope()`); тот же приём в OutboxPublisher и обоих консюмерах OrderFlow.

## 14. Middleware pipeline / фильтры / контракт API — AspNetLab, GrpcLab

- `Middleware/CorrelationIdMiddleware.cs`, `Middleware/RequestTimingMiddleware.cs`; порядок в `Program.cs`: ExceptionHandler → StatusCodePages → CorrelationId → Timing → MapControllers.
- Action-фильтр: `Filters/ValidateRecipientAttribute.cs` — валидация по двум полям, короткое замыкание; отличие фильтра от middleware (после роутинга/биндинга).
- ProblemDetails (RFC 9457) + correlationId в каждой ошибке — `Program.cs`.
- gRPC-аналоги: `GrpcLab/src/GrpcLab.Server/Services/LoggingInterceptor.cs` (interceptor), `DebtsService.cs` (RpcException+StatusCode, deadline, server streaming).
- EF-перехват: `EfCoreLab/src/EfCoreLab.App/Data/QueryCountingInterceptor.cs` (ловля N+1 числом).

## 15. Observability — OrderFlow + ObservabilityLab

- **Traceparent через Kafka:** `Shared/Kafka/KafkaTelemetry.cs` — Inject/ExtractContext (W3C в заголовки).
- **Трейс через Outbox:** `OutboxMessage.TraceParent` + восстановление родителя в `OutboxPublisher.cs` (`ActivityContext.TryParse`) — иначе трейс рвётся на фоновом воркере (21 спан вместо 2 — `ObservabilityLab/README.md`).
- Producer/Consumer-спаны: `ActivitySource`, `ActivityKind.Producer/Consumer`, теги `messaging.*`.
- OTel + Prometheus `/metrics`: `Orders/Program.cs`, `Payments/Program.cs`, `Bank/Program.cs`.

## 16. Health checks / graceful shutdown

- Health: `AspNetLab/Program.cs` — `AddHealthChecks` + `MapHealthChecks("/health")`.
- Graceful: `KafkaConsumerService.cs` — `consumer.Close()` (мгновенный rebalance); `stoppingToken` в воркерах; `TtlCache.Dispose()` гасит свипер.
- K8s (probes, rolling/blue-green/canary, HPA, OOMKilled) — только теория: `K8sLab/README.md`.

## 17. Optimistic concurrency — EfCoreLab

- `Debt.Version` (uint) → Postgres `xmin` через `IsRowVersion()` (`Data/Entities.cs`, `CollectionsDbContext.cs`).
- `Scenarios/ConcurrencyScenario.cs` — `DbUpdateConcurrencyException` + стратегия «перечитать и повторить».

## Только теория (SystemDesign/*.md)

- Idempotency-Key на HTTP-краю, HMAC-подпись вебхуков, replay-защита, DLQ — `01-payment-gateway.md`, `04-webhooks.md`.
- Оркестраторная сага, ledger двойной записи, reconciliation — `01-payment-gateway.md`.
- Fan-out on write/read, гибрид — `05-feed.md`. Приоритетные очереди, token bucket — `03-notifications.md`.

## Матрица «паттерн → проект»

| Паттерн | Где | Статус |
|---|---|---|
| Clean/Onion, DDD, CQRS, MediatR behaviors | CleanLab | код |
| Outbox, Idempotent consumer, Saga, Kafka, Polly | OrderFlow | код |
| Channels, BackgroundService, Options, captive dep, middleware, ProblemDetails, health | AspNetLab | код |
| Cache-aside, single-flight, distributed lock, инвалидация | RedisLab | код |
| TTL-кэш со свипером | TtlCache | код |
| UoW (DbContext), optimistic concurrency, DbCommandInterceptor | EfCoreLab | код |
| gRPC interceptor, deadline, streaming, эволюция контракта | GrpcLab | код |
| Traceparent-propagation, трейс через Outbox, OTel | OrderFlow / ObservabilityLab | код + доки |
| Probes, деплой-стратегии, HPA | K8sLab | теория |
| Idempotency-Key, оркестрация, ledger, fan-out | SystemDesign | теория |

PerfLab, TestingLab, Behavioral — архитектурных паттернов не содержат (бенчмарки, тесты, soft skills).
