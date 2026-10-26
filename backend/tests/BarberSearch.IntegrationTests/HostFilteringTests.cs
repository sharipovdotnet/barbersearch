using System.Net;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Проверяет host filtering на реальном конвейере (R-47): <c>AllowedHosts</c> из
/// <c>appsettings.json</c> принимает только локальные хосты, поэтому запрос с чужим
/// заголовком <c>Host</c> получает <c>400</c>, а не обслуживается API.
/// </summary>
/// <remarks>
/// Тест намеренно ходит в <c>/health/live</c>: эндпоинт анонимный и не зависит от БД,
/// поэтому отказ приходит именно от host filtering, до маршрутизации и аутентификации.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class HostFilteringTests
{
    private readonly PostgresFixture _database;

    public HostFilteringTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Unknown_host_is_rejected_with_400_while_localhost_is_served()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var local = await app.Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, local.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Host = "evil.example.test";

        using var rejected = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
}
