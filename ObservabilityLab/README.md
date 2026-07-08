# ObservabilityLab — OpenTelemetry поверх OrderFlow

Pet-проект №8 из плана. Закрывает раздел 15 из «200 ответов»: **Наблюдаемость**.
Код живёт в **../OrderFlow** (как и велит чек-лист: «подключи OTel к сервису из п.3») —
здесь документация и маршрут.

## Что получилось

Один трейс на всю сагу — **21 спан через три сервиса**, включая Kafka и все ретраи Polly:

```
orders    POST /orders            ← HTTP-запрос клиента
orders    postgresql              ← заказ + outbox одной транзакцией
orders    orders.events publish   ← outbox-воркер (продолжил ТОТ ЖЕ трейс!)
payments  orders.events process   ← консьюмер продолжил из Kafka-заголовков
payments  POST → bank POST /charge   ×4 ← ЧЕТЫРЕ попытки: три 500 банка + успех,
payments  payments.events publish       каждый ретрай Polly — отдельный спан
orders    payments.events process ← сага замкнулась, заказ Completed
```

Метрики: Orders отдаёт `/metrics` (Prometheus scrape), там `http_server_request_duration_seconds`
по роутам + runtime-метрики (GC, пул потоков).

## Как запустить

```bash
cd ../OrderFlow
docker compose up -d --wait        # + Jaeger (16686) и Prometheus (9090)
# три терминала: Bank, Payments, Orders (как раньше)
# создай заказ, потом:
#   Jaeger UI:     http://localhost:16686  → сервис orders → Find Traces
#   Prometheus:    http://localhost:9090   → http_server_request_duration_seconds_count
```

## Что где смотреть (в ../OrderFlow)

| Тема | Где | Суть |
|---|---|---|
| Kafka-propagation | `Shared/Kafka/KafkaTelemetry.cs` | HTTP прокидывает traceparent сам, для Kafka это НАША работа: inject в заголовки у продюсера, extract у консьюмера |
| Producer/Consumer-спаны | `Shared/Outbox/OutboxPublisher.cs`, `Shared/Kafka/KafkaConsumerService.cs` | `ActivitySource` + `ActivityKind.Producer/Consumer`, семантические теги messaging.* |
| **Трейс через Outbox** | `Shared/Outbox/OutboxEntities.cs` → `TraceParent` | Главная находка проекта — см. ниже |
| Подключение SDK | `Orders/Program.cs`, `Payments/…`, `Bank/…` | `AddOpenTelemetry().WithTracing(...).WithMetrics(...)`; `AddSource("Npgsql")` — SQL-спаны бесплатно |
| Prometheus | `Orders/Program.cs` + `prometheus.yml` | pull-модель: `/metrics` + scrape config |

## Пойманный нюанс — трейс рвётся на Outbox

Первая версия давала трейс из **2 спанов** вместо 21: outbox-публикатор крутится в фоновом
цикле, у него нет родительского Activity от HTTP-запроса — «publish» начинал новый,
несвязанный трейс. Решение из учебника по распределённому трейсингу: **сохранять W3C
traceparent прямо в outbox-строке** (`Activity.Current?.Id` при записи) и восстанавливать
его как родителя при публикации. Асинхронные разрывы (очереди, outbox, фоновые джобы)
всегда требуют явного проноса контекста.

## Мостик к собесу

Три сигнала (логи/метрики/трейсы) и что когда; trace id vs correlation id (наш
CorrelationIdMiddleware из AspNetLab — ручной предок трейсинга); propagation через HTTP
(автоматически) и через очереди (руками); RED-метрики (Rate, Errors, Duration — всё есть
в `http_server_request_duration`); pull vs push у Prometheus; сэмплирование трейсов;
Activity/ActivitySource = OTel span в .NET.
