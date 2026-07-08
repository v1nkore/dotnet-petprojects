# TestingLab — интеграционные тесты на Testcontainers

Pet-проект №10 из плана. Закрывает раздел 13 из «200 ответов»: **Тестирование**.

## Предметная область

Мини-домен (долги) с тремя видами поведения, которые **невозможно честно
протестировать без реальной БД** — и 4 теста против настоящего Postgres,
который Testcontainers поднимает сам (и сам убивает через контейнер-жнец ryuk).

```bash
dotnet test    # Docker должен быть запущен; больше ничего не нужно
```

## Почему не in-memory провайдер (главный тезис проекта)

| Поведение | In-memory | Testcontainers |
|---|---|---|
| Уникальный индекс (`ContractNumber`) | молча пропускает — **зелёный тест, красный прод** | `DbUpdateException` ✓ |
| Сырой SQL (`date_trunc`, `SqlQuery`) | не исполняет SQL вовсе | работает ✓ |
| Optimistic concurrency (`xmin`) | не поддерживает | `DbUpdateConcurrencyException` ✓ |

## Устройство (`tests/DebtServiceTests.cs`)

- **Контейнер один на класс** (`IClassFixture`): старт ~2–3 сек — на каждый тест слишком
  дорого. Изоляция тестов — `TRUNCATE ... RESTART IDENTITY` перед каждым
  (`IAsyncLifetime.InitializeAsync` на тестовом классе).
- **Проверка другим контекстом**: читаем сохранённое новым `DbContext`, иначе тест
  проверяет change tracker, а не базу.
- Нюанс: в xUnit v2 `IAsyncLifetime` возвращает `Task`, в v3 — `ValueTask` (поймано сборкой).

Подробности — в **study-guide.html**.
