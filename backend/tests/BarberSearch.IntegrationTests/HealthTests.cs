using System.Net;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Health-эндпоинты на реальной мигрированной базе: <c>/health/live</c> не зависит от PostgreSQL,
/// <c>/health/ready</c> проверяет его доступность.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class HealthTests
{
    private readonly PostgresFixture _database;

    public HealthTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Live_and_ready_are_healthy_when_the_database_is_migrated()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var live = await app.Client.GetAsync("/health/live");
        var liveBody = await live.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Contains("\"status\":\"Healthy\"", liveBody, StringComparison.Ordinal);

        using var ready = await app.Client.GetAsync("/health/ready");
        var readyBody = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("\"status\":\"Healthy\"", readyBody, StringComparison.Ordinal);
        Assert.Contains("postgres", readyBody, StringComparison.OrdinalIgnoreCase);
    }
}
