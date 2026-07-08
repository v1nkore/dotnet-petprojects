namespace AspNetLab.Api.Notifications;

/// <summary>
/// Фоновый воркер: вычитывает очередь и отправляет.
/// BackgroundService (а не голый IHostedService): базовый класс сам управляет жизненным
/// циклом ExecuteAsync и токеном остановки — руками нужно было бы хранить Task и CTS.
///
/// Ключевое место проекта: воркер — SINGLETON (живёт всё время приложения),
/// а INotificationSender — SCOPED. Инжектировать его в конструктор нельзя —
/// это captive dependency (см. DiTraps/CaptiveTrap.cs, где так и сделано намеренно).
/// Правильно: на каждую единицу работы создавать scope через IServiceScopeFactory.
/// </summary>
public sealed class NotificationDispatcher(
    NotificationQueue queue,
    NotificationStore store,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NotificationDispatcher запущен");

        // ReadAllAsync завершится сам, когда токен отменится на остановке хоста
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Scope на единицу работы — аналог "scope на HTTP-запрос", только вручную.
                // Всё scoped/transient, созданное внутри, корректно утилизируется в конце using.
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();

                await sender.SendAsync(item, stoppingToken);
                store.Set(new NotificationState(item.Id, item.Request, NotificationStatus.Sent));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // штатная остановка приложения
            }
            catch (Exception ex)
            {
                // Необработанное исключение из ExecuteAsync с .NET 6 ГАСИТ ВЕСЬ ХОСТ
                // (HostOptions.BackgroundServiceExceptionBehavior.StopHost по умолчанию).
                // Поэтому: одна упавшая отправка не должна убивать воркер — ловим, логируем, едем дальше.
                logger.LogError(ex, "Не удалось отправить нотификацию {Id}", item.Id);
                store.Set(new NotificationState(item.Id, item.Request, NotificationStatus.Failed));
            }
        }

        logger.LogInformation("NotificationDispatcher остановлен");
    }
}
