using System.Net;
using System.Net.Http.Json;

namespace OrderFlow.Payments;

/// <summary>
/// Типизированный клиент «банка». Вся устойчивость (retry, circuit breaker, timeout)
/// навешана снаружи — в Program.cs через AddResilienceHandler. Клиент про НЕЁ НЕ ЗНАЕТ:
/// политика — это инфраструктурная забота, а не бизнес-логика.
/// </summary>
public sealed class BankClient(HttpClient http)
{
    public async Task<BankChargeResult> ChargeAsync(Guid orderId, decimal amount, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/charge", new { orderId, amount }, ct);

        if (response.StatusCode == HttpStatusCode.PaymentRequired) // 402 — бизнес-отказ
        {
            var body = await response.Content.ReadFromJsonAsync<DeclineBody>(ct);
            return new BankChargeResult.Declined(body?.Reason ?? "Отказ банка");
        }

        response.EnsureSuccessStatusCode(); // 5xx сюда уже не доходят — их съели ретраи

        var ok = await response.Content.ReadFromJsonAsync<ChargeBody>(ct)
                 ?? throw new InvalidOperationException("Пустой ответ банка");
        return new BankChargeResult.Success(ok.TransactionId);
    }

    private sealed record ChargeBody(string TransactionId);
    private sealed record DeclineBody(string Reason);
}

/// <summary>Результат-размеченное объединение на record'ах — вместо магии из bool и string.</summary>
public abstract record BankChargeResult
{
    public sealed record Success(string TransactionId) : BankChargeResult;
    public sealed record Declined(string Reason) : BankChargeResult;
}
