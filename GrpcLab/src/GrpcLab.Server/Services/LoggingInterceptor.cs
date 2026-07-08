using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GrpcLab.Server.Services;

/// <summary>
/// Серверный interceptor — аналог middleware, но для gRPC-вызовов:
/// видит метод, метаданные (заголовки) и типизированные запрос/ответ.
/// Здесь — тайминг + статус каждого вызова; в реале сюда же — auth, метрики, трейсинг.
/// </summary>
public sealed class LoggingInterceptor(ILogger<LoggingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await continuation(request, context);

            // Логируем только медленные вызовы — продовый паттерн: лог каждого
            // вызова на горячем пути сам становится источником латентности
            // (первый замер этого бенчмарка был испорчен именно этим).
            var elapsed = Stopwatch.GetElapsedTime(start);
            if (elapsed.TotalMilliseconds > 5)
                logger.LogWarning("{Method} → OK, но медленно: {Ms:F1} мс", context.Method, elapsed.TotalMilliseconds);

            return response;
        }
        catch (RpcException ex)
        {
            logger.LogWarning("{Method} → {Status} за {Ms:F1} мс",
                context.Method, ex.StatusCode, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }
}
