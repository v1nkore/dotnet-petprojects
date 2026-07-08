using AspNetLab.Api.Notifications;

namespace AspNetLab.Api.DiTraps;

/// <summary>
/// НАМЕРЕННО СЛОМАННЫЙ КЛАСС — учебная ловушка captive dependency.
///
/// Singleton, требующий scoped-сервис в конструкторе. Если бы контейнер это позволил,
/// scoped-инстанс оказался бы «в плену» у singleton'а навсегда: для DbContext это
/// значит один контекст на все запросы сразу — гонки, утечка change tracker'а,
/// ObjectDisposedException в случайных местах.
///
/// Благодаря ValidateOnBuild (см. Program.cs) приложение с флагом --BreakDi=true
/// падает сразу на Build() с ошибкой:
///   "Cannot consume scoped service 'INotificationSender' from singleton 'CaptiveTrap'"
///
/// Правильный паттерн — в NotificationDispatcher: IServiceScopeFactory + scope на единицу работы.
/// </summary>
public sealed class CaptiveTrap(INotificationSender sender)
{
    public INotificationSender Sender { get; } = sender;
}
