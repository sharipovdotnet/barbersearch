using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.Logout;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Rate limit на <c>POST api/auth/logout</c> (R-48): действие остаётся анонимным, но
/// ограничено политикой <c>auth</c> — 10 запросов в минуту на адрес клиента.
/// </summary>
/// <remarks>
/// Logout с телом идёт по ветке внешнего клиента: antiforgery не нужен, а неизвестный
/// refresh-токен обрабатывается идемпотентно (<c>204</c>). Одиннадцатый запрос обязан
/// получить <c>429</c> с заголовком <c>Retry-After</c> и кодом <c>rateLimit.exceeded</c>.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class LogoutRateLimitTests
{
    private readonly PostgresFixture _database;

    public LogoutRateLimitTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Eleventh_logout_in_a_minute_is_rejected_with_429()
    {
        await using var app = await TestApp.StartAsync(_database);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var allowed = await app.Client.PostAsJsonAsync(
                "api/auth/logout",
                new LogoutRequest($"rate-limit-{Guid.NewGuid():N}"));

            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        using var rejected = await app.Client.PostAsJsonAsync(
            "api/auth/logout",
            new LogoutRequest($"rate-limit-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        Assert.True(
            rejected.Headers.TryGetValues("Retry-After", out var retryAfterValues),
            "Ответ 429 должен содержать заголовок Retry-After.");

        var retryAfter = Assert.Single(retryAfterValues!);
        Assert.True(
            int.TryParse(retryAfter, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0,
            $"Retry-After должен быть положительным целым числом, получено '{retryAfter}'.");

        Assert.Equal(
            "application/problem+json",
            rejected.Content.Headers.ContentType?.MediaType);

        var body = await rejected.Content.ReadAsStringAsync();

        Assert.Contains("rateLimit.exceeded", body, StringComparison.Ordinal);
    }
}
