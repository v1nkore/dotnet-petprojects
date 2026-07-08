using System.Diagnostics;

namespace AspNetLab.Api.Middleware;

/// <summary>
/// Логирует метод, путь, статус и длительность каждого запроса.
/// Stopwatch.GetTimestamp/GetElapsedTime вместо new Stopwatch() — ноль аллокаций на запрос.
/// </summary>
public sealed class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            // finally: тайминг пишется даже если ниже по pipeline вылетело исключение
            var elapsed = Stopwatch.GetElapsedTime(start);
            logger.LogInformation("{Method} {Path} -> {StatusCode} за {ElapsedMs:F1} мс",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                elapsed.TotalMilliseconds);
        }
    }
}
