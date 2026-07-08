namespace RedisLab.App;

/// <summary>
/// «Медленная БД» — то, что мы прячем за кэшем. 200 мс на запрос + счётчик
/// обращений: все сценарии доказываются числом реальных походов в базу.
/// </summary>
public sealed class SlowDatabase
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public void ResetCalls() => Volatile.Write(ref _calls, 0);

    public async Task<string> LoadDebtorAsync(int id)
    {
        Interlocked.Increment(ref _calls);
        await Task.Delay(200);
        return $"{{\"id\":{id},\"name\":\"Должник №{id}\",\"loadedAt\":\"{DateTime.UtcNow:HH:mm:ss.fff}\"}}";
    }
}
