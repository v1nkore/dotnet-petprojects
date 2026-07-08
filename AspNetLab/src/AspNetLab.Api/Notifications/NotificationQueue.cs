using System.Threading.Channels;

namespace AspNetLab.Api.Notifications;

/// <summary>
/// Очередь «HTTP-запрос → фоновый воркер» на System.Threading.Channels —
/// стандартный producer/consumer примитив вместо самодельного lock + Queue + Monitor.Pulse.
/// Bounded: при заполнении продюсер ЖДЁТ (backpressure), а не раздувает память —
/// осознанный выбор против Unbounded, который «никогда не тормозит», пока не съест всю память.
/// </summary>
public sealed class NotificationQueue
{
    private readonly Channel<NotificationWorkItem> _channel =
        Channel.CreateBounded<NotificationWorkItem>(new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,  // один воркер-читатель — каналу можно срезать внутренние блокировки
            SingleWriter = false, // писателей много: каждый HTTP-запрос
        });

    public ValueTask EnqueueAsync(NotificationWorkItem item, CancellationToken ct) =>
        _channel.Writer.WriteAsync(item, ct);

    public IAsyncEnumerable<NotificationWorkItem> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Статусы отправки. In-memory, ConcurrentDictionary — тот же паттерн, что в TtlCache.</summary>
public sealed class NotificationStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, NotificationState> _states = new();

    public void Set(NotificationState state) => _states[state.Id] = state;

    public bool TryGet(Guid id, out NotificationState? state) => _states.TryGetValue(id, out state);
}
