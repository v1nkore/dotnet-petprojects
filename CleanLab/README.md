# CleanLab — Clean Architecture + CQRS на MediatR

Pet-проект №14 из плана. Закрывает раздел 7 из «200 ответов»: **Архитектура: DDD, Clean, CQRS**.

## Предметная область

Модуль «долги»: открыть долг, зарегистрировать платёж, посмотреть состояние.
Домен намеренно простой — вся суть в **направлении зависимостей**:

```
Api ──▶ Infrastructure ──▶ Application ──▶ Domain
(тонкий      (реализации       (сценарии,      (агрегат, value object,
транспорт)    портов)           порты, CQRS)    события, инварианты)
```

Domain не знает ни о ком. Application объявляет порты (`IDebtRepository`, `IDebtReadStore`) —
Infrastructure их реализует. Это и есть «развернуть зависимости внутрь».

## Как запустить

```bash
dotnet test                                        # 6 тестов: домен без инфраструктуры вообще
dotnet run --project src/CleanLab.Api -c Release   # HTTP на :5400

curl -X POST localhost:5400/debts -H "Content-Type: application/json" -d '{"contractNumber":"К-1","amount":1000}'
curl -X POST localhost:5400/debts/{id}/payments -H "Content-Type: application/json" -d '{"amount":1000}'
curl localhost:5400/debts/{id}    # → isClosed: true
```

## Что где смотреть

| Концепция | Где | Суть |
|---|---|---|
| Value object | `Domain/Debt.cs` → `Money` | Равенство по значению, иммутабельность, инвариант «нельзя сложить RUB и USD» вместо голого decimal |
| Агрегат | `Domain/Debt.cs` → `Debt` | Приватные сеттеры, фабрика `Open`, все инварианты внутри — некорректное состояние недостижимо снаружи |
| Доменные события | там же | Агрегат фиксирует ФАКТ (`DebtClosed`), реакция (SMS) — в подписчике application-слоя; публикация после сохранения |
| CQRS | `Application/Debts.cs` | Команды меняют через агрегат, запрос читает напрямую в DTO мимо инвариантов; одно хранилище — CQRS ≠ две базы |
| Порты и адаптеры | `Application` ↔ `Infrastructure` | Интерфейсы у потребителя, реализация снаружи; in-memory меняется на EF без единой правки Domain/Application |
| Pipeline behavior | `LoggingBehavior` | Cross-cutting для команд — аналог middleware |
| Тонкий транспорт | `Api/Program.cs` | Endpoint = принять → `mediator.Send` → вернуть; `DomainException` → 422 маппится тут |

## Где это оверинжиниринг (честная секция)

Для CRUD без инвариантов эти 4 проекта — чистый налог: слои ради слоёв.
Clean/DDD окупается, когда есть **настоящие инварианты** (как «нельзя переплатить долг»),
несколько транспортов, долгая жизнь кода и команда >2 человек. Секрет senior-ответа:
уметь сказать, когда паттерн НЕ нужен.

Подробности — в **study-guide.html**.
