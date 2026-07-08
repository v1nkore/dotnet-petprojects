using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace OrderFlow.Shared.Kafka;

/// <summary>
/// Сквозной трейсинг через Kafka. HTTP-инструментация прокидывает контекст
/// сама (заголовок traceparent), а для Kafka это НАША работа: продюсер кладёт
/// traceparent в заголовки сообщения, консьюмер достаёт и продолжает трейс —
/// иначе трейс «рвётся» на брокере и в Jaeger будет два несвязанных куска.
/// </summary>
public static class KafkaTelemetry
{
    public const string SourceName = "OrderFlow.Kafka";

    // ActivitySource — «издатель» спанов. Activity = span в терминах .NET;
    // OTel SDK подписывается на источники по имени (AddSource в Program.cs).
    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Продюсер: упаковать текущий контекст трейса в заголовки сообщения (W3C traceparent).</summary>
    public static Headers InjectContext(Activity? activity)
    {
        var headers = new Headers();
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(activity?.Context ?? default, Baggage.Current),
            headers,
            static (headers, key, value) => headers.Add(key, Encoding.UTF8.GetBytes(value)));
        return headers;
    }

    /// <summary>Консьюмер: достать контекст родителя из заголовков — спан обработки станет продолжением трейса.</summary>
    public static PropagationContext ExtractContext(Headers? headers) =>
        Propagators.DefaultTextMapPropagator.Extract(default, headers,
            static (headers, key) =>
                headers is not null && headers.TryGetLastBytes(key, out var bytes)
                    ? [Encoding.UTF8.GetString(bytes)]
                    : Array.Empty<string>());
}
