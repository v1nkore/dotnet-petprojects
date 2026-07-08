using GrpcLab.Server.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Один сервер — два протокола на разных портах:
//   5501 — HTTP/2 без TLS для gRPC (h2c; gRPC работает ТОЛЬКО поверх HTTP/2:
//          мультиплексирование стримов в одном соединении — его фундамент)
//   5502 — HTTP/1.1 для REST-эндпоинта сравнения
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenLocalhost(5501, o => o.Protocols = HttpProtocols.Http2);
    kestrel.ListenLocalhost(5502, o => o.Protocols = HttpProtocols.Http1);
});

builder.Services.AddGrpc(o => o.Interceptors.Add<LoggingInterceptor>());
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();

app.MapGrpcService<DebtsService>();

// Тот же ответ, что GetDebt, но REST/JSON — для честного замера в клиенте
app.MapGet("/debts/{id:int}", (int id) => Results.Ok(new
{
    debtId = id,
    debtorName = $"Должник №{id}",
    principalMinorUnits = 150_000_00L,
    currency = "RUB",
    status = "Active",
}));

app.Run();
