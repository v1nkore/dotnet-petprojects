using AspNetLab.Api.DiTraps;
using AspNetLab.Api.Middleware;
using AspNetLab.Api.Notifications;

var builder = WebApplication.CreateBuilder(args);

// ValidateScopes/ValidateOnBuild по умолчанию включены только в Development.
// Включаем всегда: ловить captive dependency на старте дешевле, чем в проде в 3 часа ночи.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

// JsonStringEnumConverter: enum'ы в JSON строками ("Email"), а не магическими числами —
// иначе контракт API нечитаем и ломается при перестановке членов enum
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ProblemDetails (RFC 9457) — стандартный формат тела для всех ошибок.
// Дописываем correlationId в каждый ответ-проблему: по нему саппорт находит логи.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["correlationId"] =
        ctx.HttpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());

// Домен: очередь в памяти + статусы + фоновый отправитель
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddSingleton<NotificationStore>();
builder.Services.AddHostedService<NotificationDispatcher>();

// Scoped-сервис. Воркер-singleton будет брать его ПРАВИЛЬНО — через IServiceScopeFactory.
builder.Services.AddScoped<INotificationSender, FakeEmailSender>();

// Options pattern: конфиг → типизированный объект, валидация НА СТАРТЕ,
// а не при первом обращении глубоко в рантайме
builder.Services.AddOptions<DispatcherOptions>()
    .BindConfiguration(DispatcherOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Health check для k8s-проб (см. PetProjects/K8sLab)
builder.Services.AddHealthChecks();

// Ловушка: singleton, зависящий от scoped. Включается флагом:
//   dotnet run --project src/AspNetLab.Api -- --BreakDi=true
// Благодаря ValidateOnBuild приложение упадёт сразу на Build() с внятной ошибкой.
if (builder.Configuration.GetValue<bool>("BreakDi"))
    builder.Services.AddSingleton<CaptiveTrap>();

var app = builder.Build();

// Порядок pipeline важен и читается сверху вниз:
// обработчик ошибок — первым (чтобы поймать всё, что ниже),
// correlation id — до логирования (чтобы таймингам достался scope с id).
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestTimingMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Точка входа с top-level statements генерирует internal Program —
// partial-объявление делает его видимым для WebApplicationFactory<Program> в тестах
public partial class Program;
