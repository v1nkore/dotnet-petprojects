using System.ComponentModel.DataAnnotations;

namespace AspNetLab.Api.Notifications;

public enum NotificationChannel
{
    Email,
    Sms,
}

/// <summary>
/// Входной контракт. DataAnnotations закрывают «форму» (обязательность, длину) —
/// их проверяет [ApiController] автоматически. Бизнес-правило «получатель соответствует
/// каналу» живёт в action-фильтре ValidateRecipientAttribute.
///
/// Ловушка позиционных record'ов: атрибуты валидации должны висеть на ПАРАМЕТРЕ
/// конструктора (без target'а), а не на свойстве ([property: Required]) — иначе MVC
/// бросает InvalidOperationException «validation metadata … must be associated with
/// the constructor parameter» прямо в рантайме при первом биндинге.
/// </summary>
public sealed record NotificationRequest(
    NotificationChannel Channel,
    [Required, StringLength(200)] string Recipient,
    [Required, StringLength(2000)] string Text);

public enum NotificationStatus
{
    Queued,
    Sent,
    Failed,
}

public sealed record NotificationState(Guid Id, NotificationRequest Request, NotificationStatus Status);

/// <summary>Единица работы, уходящая в очередь.</summary>
public sealed record NotificationWorkItem(Guid Id, NotificationRequest Request);
