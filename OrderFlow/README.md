# OrderFlow — Kafka, Outbox, Saga и Polly в одном домене

Pet-проекты №2 и №3 из плана подготовки, объединённые в один связный домен.
Закрывает разделы 8–9 из «200 ответов»: **Микросервисы и распределённые системы**
и **Очереди сообщений: Kafka**.

## Предметная область

Оформление заказа с оплатой — минимальная распределённая транзакция из жизни:

```
клиент → POST /orders → [Orders]  --OrderCreated-->  [Payments] → POST /charge → [Bank 💥]
                            ↑                             |
                            └──── PaymentSucceeded / PaymentFailed ────┘
        Completed ← оплата прошла          Cancelled ← КОМПЕНСАЦИЯ: оплата не прошла
```

- **Orders** (`:5300`) — принимает заказы, ведёт их статус, слушает исходы оплат.
- **Payments** — слушает заказы, списывает деньги через «банк», публикует исход.
- **Bank** (`:5301`) — намеренно нестабильный внешний сервис: ~35% ответов — 500,
  изредка «зависает» на 5 секунд, суммы ≥ 5000 отклоняет (402). Именно таким мир
  видится из продакшена — и именно поэтому в Payments стоит Polly.

Все события идут через **Kafka** (`orders.events`, `payments.events`), каждое —
через **Outbox** в Postgres, каждый консьюмер **идемпотентен**.

## Как запустить

```bash
docker compose up -d --wait      # Kafka (KRaft, без ZooKeeper) + Postgres

# три терминала:
dotnet run --project src/OrderFlow.Bank     -c Release
dotnet run --project src/OrderFlow.Payments -c Release
dotnet run --project src/OrderFlow.Orders   -c Release

# счастливый путь → Completed (возможно, после ретраев)
curl -X POST http://localhost:5300/orders -H "Content-Type: application/json" \
  -d '{"customerEmail":"user@example.com","amount":100}'

# компенсация → Cancelled (банк отклоняет суммы >= 5000)
curl -X POST http://localhost:5300/orders -H "Content-Type: application/json" \
  -d '{"customerEmail":"vip@example.com","amount":7000}'

curl http://localhost:5300/orders/{id}     # следить за статусом
```

## Что где смотреть

| Паттерн | Файл | Суть |
|---|---|---|
| **Outbox** (запись) | `Orders/Program.cs` → POST /orders | Заказ + событие одним `SaveChanges` = одна транзакция; событие не может потеряться |
| **Outbox** (публикация) | `Shared/Outbox/OutboxPublisher.cs` | Генерик-воркер: опрос таблицы → Kafka → отметка `PublishedAtUtc`; at-least-once |
| **Идемпотентный консьюмер** | `Payments/OrderEventsConsumer.cs` | Проверка `ProcessedMessage` ДО побочных эффектов; бизнес-эффект + отметка + исходящее событие в одной транзакции |
| **Saga (хореография)** | `Orders/Consumers/PaymentEventsConsumer.cs` | Никто не оркестрирует: сервисы реагируют на события; `PaymentFailed` → `Cancelled` — компенсирующая транзакция |
| **Kafka-консьюмер как надо** | `Shared/Kafka/KafkaConsumerService.cs` | Выделенный поток (Consume блокирующий), ручной коммит после обработки, `Close()` при остановке |
| **Партиционирование** | `Shared/Contracts/Events.cs` | Ключ сообщения = OrderId → все события заказа в одной партиции → строгий порядок |
| **Polly v8** | `Payments/Program.cs` | total timeout → retry (exp + jitter) → circuit breaker → attempt timeout; порядок стратегий — семантика |
| **Продюсер без потерь** | `Shared/Outbox/OutboxPublisher.cs` → `KafkaSetup` | `Acks.All` + `EnableIdempotence` |

## Реальный прогон (из логов этой машины)

```
Charge 0cafd77f: 500 (transient)      ← банк упал
Charge 0cafd77f: 500 (transient)      ← Polly: ретрай №1 — снова упал
Charge 0cafd77f: OK tx-73e165f4       ← ретрай №2 — успех, заказ Completed

Charge 9d3310ed: имитируем зависание  ← банк завис на 5с
Charge 9d3310ed: OK tx-83d5f388       ← attempt timeout (1с) отсёк, ретрай — успех

Charge 71d777d1: отказ — лимит        ← 402: бизнес-отказ, Polly НЕ ретраит,
                                        сага завершается компенсацией → Cancelled
```

7 заказов: 6 Completed (сквозь случайные 500-е), 1 Cancelled по лимиту. Ни одного потерянного.

## Ключевые «почему» (см. комментарии в коде)

- **Почему нельзя просто `producer.Produce` после `SaveChanges`?** Между ними процесс
  может умереть: заказ есть, события нет — сага не начнётся никогда. Outbox закрывает
  эту дыру ценой at-least-once → отсюда идемпотентность.
- **Почему компенсация, а не rollback?** Распределённого rollback'а не существует:
  деньги уже (не) списаны в чужой системе. Отменяем бизнес-эффект новым действием.
- **Почему 402 не ретраится, а 500 ретраится?** Transient-ошибка может пройти со
  второго раза; бизнес-отказ детерминирован — ретрай лишь добьёт банк и отложит компенсацию.
- **Почему ключ партиционирования = OrderId?** Kafka гарантирует порядок только внутри
  партиции. Без ключа события одного заказа разлетятся по партициям и могут обогнать друг друга.

Подробный план изучения — в **study-guide.html**.
