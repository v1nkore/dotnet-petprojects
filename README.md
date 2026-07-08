# PetProjects — карта подготовки к собеседованию Senior .NET

Каждый проект привязан к разделам файла **«200 ответов · Senior Backend .NET»**
и к пунктам чек-листа из `../Pet-проекты_и_чеклисты.md`. Порядок проектов = порядок
разделов в 200 ответах: изучаешь раздел → разбираешь проект → возвращаешься к вопросам.

| # | Проект | Разделы «200 ответов» | Чек-лист | Статус |
|---|---|---|---|---|
| 1 | **[TtlCache](TtlCache/)** — потокобезопасный кэш с TTL + бенчмарки | 1. C# и .NET runtime · 2. Async и многопоточность | п.1 | ✅ готов |
| 2 | **[AspNetLab](AspNetLab/)** — middleware, фильтры, DI-ловушки, BackgroundService | 3. ASP.NET Core | п.5 | ✅ готов |
| 3 | **[EfCoreLab](EfCoreLab/)** — N+1, change tracking, xmin, EXPLAIN ANALYZE | 4. EF Core · 5. Базы данных и SQL | п.6 | ✅ готов |
| 4 | **[OrderFlow](OrderFlow/)** — Kafka, Outbox, идемпотентность, Saga, Polly | 8. Микросервисы · 9. Очереди сообщений | п.2 + п.3 | ✅ готов |
| 5 | **[GrpcLab](GrpcLab/)** — gRPC vs REST с замером, streaming, deadline, interceptor | 10. API: REST и gRPC | п.7 | ✅ готов |
| 6 | **[RedisLab](RedisLab/)** — cache-aside, stampede (3 защиты), инвалидация | 11. Кэширование и Redis | п.13 | ✅ готов |
| 7 | **[TestingLab](TestingLab/)** — Testcontainers, реальный Postgres в тесте | 13. Тестирование | п.10 | ✅ готов |
| 8 | **[CleanLab](CleanLab/)** — Clean Architecture + CQRS (MediatR), агрегат, value object, доменные события | 7. Архитектура: DDD, Clean, CQRS | п.14 | ✅ готов |
| 9 | **[K8sLab](K8sLab/)** — kind, Deployment/probes/limits, Helm, откат вживую, CI-пайплайн | 12. Контейнеры, K8s, CI/CD | п.9 | ✅ готов |
| 10 | **[PerfLab](PerfLab/)** — Span/аллокации + NBomber, thread pool starvation вживую | 14. Производительность | п.11–12 | ✅ готов |
| 11 | **[ObservabilityLab](ObservabilityLab/)** — OTel поверх OrderFlow: сквозной трейс через Kafka, Jaeger, Prometheus | 15. Наблюдаемость | п.8 | ✅ готов |
| 12 | **[SystemDesign](SystemDesign/)** — 5 письменных разборов (платёжный шлюз, shortener, нотификации, вебхуки, feed) | 16. System design | п.4 | ✅ готов |
| 13 | **[SolidMap](SolidMap/)** — SOLID и паттерны на живых примерах из этих же проектов | 6. ООП, SOLID, паттерны | — | ✅ готов |
| 14 | **[Behavioral](Behavioral/)** — каркасы STAR-историй на 6 вопросов + антипаттерны | 17. Поведенческие | п.15 | ✅ каркасы (наполнить своими историями) |

Все 17 разделов «200 ответов» закрыты: 11 кодовых проектов + 3 письменных модуля.
Behavioral требует твоего участия: каркасы готовы, истории — твои.

## Как устроен каждый проект

- **README.md** — предметная область, зачем, как запустить, реальные цифры с этой машины.
- **study-guide.html** — план ознакомления (открой в браузере): порядок чтения кода,
  разбор каждого решения «почему так», языковые фишки по версиям, пойманные баги
  как истории для собеса, мостик к вопросам, идеи масштабирования.
- Код закрывает базовую проблему домена и снабжён комментариями «почему», а не «что».

## Быстрый старт

```bash
# TtlCache и AspNetLab — без инфраструктуры:
cd TtlCache  && dotnet test && dotnet run --project src/TtlCache.Demo -c Release
cd AspNetLab && dotnet test && dotnet run --project src/AspNetLab.Api

# EfCoreLab и OrderFlow — сначала docker compose up -d в папке проекта (Postgres / Kafka)
```

## Рабочий ритм (из чек-листа)

1. Один проект = одна волна в 1–2 недели.
2. После разбора — конспект «что понял» в 5–10 строк своими словами.
3. Параллельно собеседования: поплыл на вопросе → пункт в план следующей недели.
