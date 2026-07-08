using System.Net;
using System.Net.Http.Json;
using AspNetLab.Api.Middleware;
using AspNetLab.Api.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetLab.Tests;

/// <summary>
/// Интеграционные тесты через WebApplicationFactory: приложение поднимается
/// ЦЕЛИКОМ (реальный DI, реальный pipeline, реальный воркер), но in-memory,
/// без сокетов — HttpClient ходит напрямую в TestServer.
/// IClassFixture — одна фабрика на весь класс: хост стартует один раз, тесты быстрые.
/// </summary>
public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Сервер сериализует enum'ы строками (JsonStringEnumConverter в Program.cs) —
    // клиент в тестах должен читать теми же правилами, иначе JsonException.
    // Урок: настройки (де)сериализации — часть контракта API.
    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

    [Fact]
    public async Task Response_ContainsGeneratedCorrelationId_WhenClientSentNone()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/notifications/00000000-0000-0000-0000-000000000001");

        var header = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.False(string.IsNullOrWhiteSpace(header));
    }

    [Fact]
    public async Task Response_EchoesClientCorrelationId()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/notifications/00000000-0000-0000-0000-000000000001");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "my-trace-42");

        var response = await client.SendAsync(request);

        Assert.Equal("my-trace-42", Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
    }

    [Fact]
    public async Task Post_InvalidEmailRecipient_Returns400WithValidationProblem()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/notifications",
            new NotificationRequest(NotificationChannel.Email, "не-адрес", "привет"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("Recipient", problem.Errors.Keys);
    }

    [Fact]
    public async Task Post_ValidNotification_Returns202_AndWorkerEventuallySendsIt()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/notifications",
            new NotificationRequest(NotificationChannel.Email, "user@example.com", "привет"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);

        // Воркер асинхронный — опрашиваем статус с дедлайном вместо слепого Delay
        var deadline = DateTime.UtcNow.AddSeconds(10);
        NotificationState? state = null;
        while (DateTime.UtcNow < deadline)
        {
            state = await client.GetFromJsonAsync<NotificationState>(location, Json);
            if (state?.Status == NotificationStatus.Sent)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(NotificationStatus.Sent, state?.Status);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/notifications/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>
/// Captive dependency: с флагом BreakDi=true контейнер регистрирует singleton,
/// зависящий от scoped, — и благодаря ValidateOnBuild хост падает на старте,
/// а не молча раздаёт всем один «пленённый» инстанс.
/// </summary>
public class CaptiveDependencyTests
{
    [Fact]
    public void Host_FailsOnBuild_WhenSingletonDependsOnScoped()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("BreakDi", "true"));

        // Хост строится лениво — первое обращение к Services триггерит Build()
        var ex = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(ex);
        Assert.Contains("Cannot consume scoped service", ex.ToString());
        Assert.Contains("CaptiveTrap", ex.ToString());
    }
}
