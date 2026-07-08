using RedisLab.App;
using StackExchange.Redis;

// ConnectionMultiplexer — ОДИН на приложение (как HttpClient):
// внутри одно мультиплексированное соединение, потокобезопасен, дорог в создании
var mux = await ConnectionMultiplexer.ConnectAsync("localhost:6380,allowAdmin=true");
var redis = mux.GetDatabase();

// Чистый старт для повторяемости сценариев
await mux.GetServers().First().FlushDatabaseAsync();

var db = new SlowDatabase();

await Scenarios.CacheAsideAsync(redis, db);
await Scenarios.StampedeAsync(redis, db);
await Scenarios.InvalidationAsync(redis, db);

Console.WriteLine("\n=== Готово ===");
