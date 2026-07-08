using Microsoft.Extensions.Primitives;

namespace AspNetLab.Api.Middleware;

/// <summary>
/// Сквозной идентификатор запроса: берём из заголовка клиента (если пришёл межсервисный вызов)
/// или генерируем свой. Кладём в заголовок ответа и в log scope — все логи ниже по стеку
/// автоматически получают CorrelationId без передачи через параметры.
/// Middleware по convention: класс с ctor(RequestDelegate, ...DI) и методом InvokeAsync.
/// Создаётся ОДИН на приложение (по сути singleton) — нельзя хранить в полях состояние запроса.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId =
            context.Request.Headers.TryGetValue(HeaderName, out var fromClient)
            && !StringValues.IsNullOrEmpty(fromClient)
                ? fromClient.ToString()
                : Guid.NewGuid().ToString("N");

        // Заголовки можно трогать только до первого байта тела — поэтому ставим до next()
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
