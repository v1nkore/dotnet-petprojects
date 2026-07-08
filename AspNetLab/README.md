# AspNetLab — ASP.NET Core под капотом

Pet-проект №5 из плана подготовки. Закрывает раздел 3 из «200 ответов»: **ASP.NET Core**
(pipeline, DI lifetimes, captive dependency, Options, `BackgroundService`, `ProblemDetails`).

## Предметная область

Мини-сервис нотификаций: `POST /notifications` принимает сообщение (email/sms), кладёт его
во внутреннюю очередь (`System.Threading.Channels`) и сразу отвечает `202 Accepted`;
фоновый воркер вычитывает очередь и «отправляет». `GET /notifications/{id}` — статус.

Домен нарочно крошечный — он лишь каркас, на котором живут все механизмы из чек-листа:

| Механизм | Где | Что демонстрирует |
|---|---|---|
| Middleware по convention | `Middleware/CorrelationIdMiddleware.cs` | correlation id: из заголовка или новый, в ответ + log scope |
| Ещё middleware | `Middleware/RequestTimingMiddleware.cs` | тайминг запросов через `Stopwatch.GetTimestamp` (0 аллокаций), `finally` — лог даже при исключении |
| Порядок pipeline | `Program.cs` | ExceptionHandler → StatusCodePages → CorrelationId → Timing → MapControllers, и почему именно так |
| Кастомный action-фильтр | `Filters/ValidateRecipientAttribute.cs` | бизнес-валидация по двум полям; чем фильтр отличается от middleware; короткое замыкание через `context.Result` |
| `BackgroundService` | `Notifications/NotificationDispatcher.cs` | scope на единицу работы через `IServiceScopeFactory`; почему необработанное исключение гасит весь хост |
| Captive dependency | `DiTraps/CaptiveTrap.cs` | singleton ← scoped: запусти с `--BreakDi=true` и получи ошибку на старте благодаря `ValidateOnBuild` |
| Options pattern | `Notifications/DispatcherOptions.cs` | `BindConfiguration` + `ValidateDataAnnotations` + `ValidateOnStart` |
| `ProblemDetails` (RFC 9457) | `Program.cs` | единый формат ошибок, correlationId дописывается в каждую проблему |
| Producer/consumer | `Notifications/NotificationQueue.cs` | bounded `Channel` и осознанный backpressure вместо unbounded-очереди |

## Как запустить

```bash
dotnet test                                          # 6 интеграционных тестов (WebApplicationFactory)
dotnet run --project src/AspNetLab.Api               # обычный запуск
dotnet run --project src/AspNetLab.Api -- --BreakDi=true   # демонстрация captive dependency: падение на старте

# пример запроса
curl -i -X POST http://localhost:5132/notifications -H "Content-Type: application/json" \
  -d '{"channel":"Email","recipient":"user@example.com","text":"привет"}'
```

## Два реальных бага, пойманных при разработке (обе — готовые истории на собес)

1. **`[property: Required]` на позиционном record** вешает атрибут на сгенерированное
   свойство, а MVC-валидация требует его на параметре конструктора — и бросает
   `InvalidOperationException` в рантайме при первом биндинге. Правильно: `[Required] string Recipient`
   без target'а.
2. **Настройки JSON — часть контракта API.** Включили `JsonStringEnumConverter` на сервере —
   клиент в тестах перестал парсить ответы дефолтными настройками. Клиент и сервер должны
   договориться об одних правилах сериализации.

Подробный план изучения — в **study-guide.html**.
