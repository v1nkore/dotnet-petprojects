using System.Diagnostics;
using System.Net.Http.Json;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcLab.Contracts;

// Канал — аналог HttpClient: тяжёлый, потокобезопасный, ОДИН на приложение.
// Внутри — одно HTTP/2-соединение с мультиплексированием запросов.
using var channel = GrpcChannel.ForAddress("http://localhost:5501");
var grpcClient = new Debts.DebtsClient(channel);

using var restClient = new HttpClient { BaseAddress = new Uri("http://localhost:5502") };

Console.WriteLine("═══ 1. Unary-вызов ═══\n");
var debt = await grpcClient.GetDebtAsync(new GetDebtRequest { DebtId = 42 });
Console.WriteLine($"{debt.DebtorName}: {debt.PrincipalMinorUnits / 100m:N2} {debt.Currency}, {debt.Status}\n");

Console.WriteLine("═══ 2. Латентность: gRPC vs REST (2000 последовательных вызовов) ═══\n");
const int warmup = 200, n = 2000;

var grpcTimes = await Measure(warmup, n, async () =>
    await grpcClient.GetDebtAsync(new GetDebtRequest { DebtId = 1 }));

var restTimes = await Measure(warmup, n, async () =>
    await restClient.GetFromJsonAsync<RestDebt>("/debts/1"));

Print("gRPC (protobuf/HTTP2)", grpcTimes);
Print("REST (JSON/HTTP1.1)  ", restTimes);

Console.WriteLine("""

Сюрприз: на localhost с крошечным сообщением REST обычно БЫСТРЕЕ —
фиксированная цена HTTP/2-фрейминга перевешивает экономию protobuf,
а мультиплексирование в последовательном цикле не даёт ничего.
gRPC выигрывает там, где: payload большой (protobuf в разы компактнее),
сеть реальная (одно соединение вместо переустановок), вызовы параллельные
(HTTP/2 мультиплексирует, HTTP/1.1 занимает соединение целиком) и есть стриминг.
Мораль для собеса: «gRPC быстрее REST» — не аксиома, а функция от профиля нагрузки.
""");

Console.WriteLine("═══ 3. Server streaming ═══\n");
using (var call = grpcClient.StreamPayments(new StreamPaymentsRequest { DebtId = 42, Count = 5 }))
{
    await foreach (var payment in call.ResponseStream.ReadAllAsync())
        Console.WriteLine($"  платёж {payment.AmountMinorUnits / 100m:N2} от {payment.PaidAt.ToDateTime():HH:mm:ss.fff}");
}

Console.WriteLine("\n═══ 4. Deadline ═══\n");
// Deadline — абсолютный потолок на вызов. Едет с запросом (заголовок grpc-timeout):
// по истечении клиент получает DeadlineExceeded, а СЕРВЕР — отменённый
// CancellationToken, то есть перестаёт делать бесполезную работу.
try
{
    using var call = grpcClient.StreamPayments(
        new StreamPaymentsRequest { DebtId = 42, Count = 100 }, // ~10 секунд работы
        deadline: DateTime.UtcNow.AddMilliseconds(350));

    var received = 0;
    await foreach (var _ in call.ResponseStream.ReadAllAsync())
        received++;
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
{
    Console.WriteLine("Поток оборван: DeadlineExceeded — клиент не ждёт, сервер не работает впустую.");
}

Console.WriteLine("\n═══ Готово ═══");

static async Task<double[]> Measure(int warmup, int n, Func<Task> call)
{
    for (var i = 0; i < warmup; i++) await call();

    var times = new double[n];
    for (var i = 0; i < n; i++)
    {
        var start = Stopwatch.GetTimestamp();
        await call();
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    Array.Sort(times);
    return times;
}

static void Print(string label, double[] sorted)
{
    Console.WriteLine($"{label}  p50: {sorted[sorted.Length / 2]:F3} мс · " +
                      $"p95: {sorted[(int)(sorted.Length * 0.95)]:F3} мс · " +
                      $"p99: {sorted[(int)(sorted.Length * 0.99)]:F3} мс");
}

internal sealed record RestDebt(int DebtId, string DebtorName, long PrincipalMinorUnits, string Currency, string Status);
