# SolidMap — ООП, SOLID и паттерны на живых примерах из твоих проектов

Раздел 6 из «200 ответов» — теория, но отвечать её лучше примерами из кода,
который ты «писал». Каждая буква и паттерн ниже — ссылка на реальное место в PetProjects.

## SOLID

| Принцип | Живой пример | Где |
|---|---|---|
| **S**RP | `CorrelationIdMiddleware` делает одно: correlation id. Тайминги — отдельный middleware, хотя «можно было в один» | AspNetLab/Middleware |
| **O**CP | Новый сценарий в CleanLab = новый handler; существующие не трогаются. Новая стратегия кэша в бенчмарке = новый класс за `IBenchCache` | CleanLab/Application, TtlCache/benchmarks |
| **L**SP | Все три кэша бенчмарка взаимозаменяемы за `IBenchCache` — бенчмарк не знает, кто внутри. Нарушение LSP сломало бы замер | TtlCache/benchmarks/Caches.cs |
| **I**SP | `IDebtRepository` (запись) и `IDebtReadStore` (чтение) — раздельные узкие порты, хотя реализует их один класс. Query-handler не видит методов записи | CleanLab/Application |
| **D**IP | Application объявляет порты, Infrastructure реализует — зависимость направлена от деталей к абстракции. Вся суть Clean Architecture | CleanLab целиком |

## Паттерны GoF, которые ты можешь показать в коде

| Паттерн | Где | Как |
|---|---|---|
| **Strategy** | TtlCache/benchmarks | три стратегии синхронизации за одним интерфейсом |
| **Template Method** | OrderFlow/Shared/KafkaConsumerService | базовый класс держит цикл/коммиты, наследник реализует только `HandleAsync` |
| **Decorator** | OrderFlow/Payments Polly-pipeline | resilience-handler'ы оборачивают HttpClient слоями, клиент не знает |
| **Observer** | CleanLab доменные события; MediatR notifications | подписчики реагируют на факт, издатель их не знает |
| **Factory Method** | CleanLab `Debt.Open(...)` | валидация + событие в точке создания, ctor приватный |
| **Chain of Responsibility** | AspNetLab pipeline; MediatR behaviors | каждый узел решает: обработать/передать дальше/оборвать |
| **Proxy** | EfCoreLab lazy-loading proxies | прокси наследует сущность и подкладывает запрос к БД в геттер (и это источник N+1!) |
| **Repository / Unit of Work** | CleanLab порты; сам DbContext — UoW | «DbContext уже UoW, а DbSet уже репозиторий» — хороший дискуссионный ответ |
| **Singleton (как lifetime, не как класс)** | везде DI | анти-паттерн «класс-синглтон» vs нормальный singleton через контейнер |

## Архитектурные паттерны (не GoF, но спросят)

Outbox, Saga, Circuit Breaker, Retry+Jitter, Idempotent Consumer, Cache-Aside,
CQRS — все реализованы в OrderFlow/RedisLab/CleanLab, разборы в их study-guide'ах.

## Как отвечать на «расскажи про SOLID»

Не определениями, а парой «принцип → своя история»: *«ISP — у меня в CQRS-модуле
чтение и запись это два интерфейса, реализованных одним классом: query-handler
физически не может позвать Save»*. Одна конкретика дороже пяти определений.
