using AspNetLab.Api.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AspNetLab.Api.Filters;

/// <summary>
/// Кастомный action-фильтр: бизнес-валидация «получатель соответствует каналу»,
/// которую не выразить DataAnnotations на одном свойстве (правило смотрит на ДВА поля).
///
/// Фильтры — это MVC-слой поверх middleware: они выполняются ПОСЛЕ маршрутизации
/// и биндинга модели, поэтому здесь уже доступны типизированные аргументы action'а —
/// middleware на этом этапе видел бы только сырой поток байтов.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ValidateRecipientAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.ActionArguments.Values.OfType<NotificationRequest>().FirstOrDefault();
        if (request is null)
            return;

        var error = request.Channel switch
        {
            NotificationChannel.Email when !request.Recipient.Contains('@')
                => "Для канала Email получатель должен быть e-mail адресом.",
            NotificationChannel.Sms when !request.Recipient.All(c => char.IsDigit(c) || c == '+')
                => "Для канала Sms получатель должен быть номером телефона.",
            _ => null,
        };

        if (error is not null)
        {
            var problems = new ValidationProblemDetails(
                new Dictionary<string, string[]> { [nameof(request.Recipient)] = [error] });

            // Установка Result коротко замыкает pipeline: action не выполнится вовсе
            context.Result = new BadRequestObjectResult(problems);
        }
    }
}
