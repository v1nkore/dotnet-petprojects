using Microsoft.Extensions.Options;

namespace AspNetLab.Api.Notifications;

/// <summary>
/// Scoped-сервис — как реальный отправитель, у которого под капотом был бы
/// DbContext или HttpClient с per-request состоянием.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(NotificationWorkItem item, CancellationToken ct);
}

public sealed class FakeEmailSender(
    IOptions<DispatcherOptions> options,
    ILogger<FakeEmailSender> logger) : INotificationSender
{
    public async Task SendAsync(NotificationWorkItem item, CancellationToken ct)
    {
        // Имитация сетевого вызова к SMTP/SMS-шлюзу
        await Task.Delay(options.Value.SendDelayMilliseconds, ct);
        logger.LogInformation("Отправлено {Channel} для {Recipient}: {Text}",
            item.Request.Channel, item.Request.Recipient, item.Request.Text);
    }
}
