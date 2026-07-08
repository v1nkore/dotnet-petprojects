using System.ComponentModel.DataAnnotations;

namespace AspNetLab.Api.Notifications;

/// <summary>
/// Options pattern: секция конфига → типизированный класс.
/// ValidateDataAnnotations + ValidateOnStart в Program.cs означают:
/// кривой конфиг валит приложение НА СТАРТЕ, а не при первом сообщении в 3 часа ночи.
/// </summary>
public sealed class DispatcherOptions
{
    public const string SectionName = "Dispatcher";

    [Range(0, 10_000)]
    public int SendDelayMilliseconds { get; set; } = 200;
}
