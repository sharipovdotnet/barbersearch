using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BarberSearch.Web.Deployment;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Порядок middleware аутентификации и rate limiter (R-53): глобальный лимит записи
/// партиционируется по claim <c>sub</c>, поэтому <c>UseRateLimiter</c> обязан идти после
/// <c>UseAuthentication</c>.
/// </summary>
/// <remarks>
/// <c>RateLimitSettings.WritePartitionKey</c> читает <c>ClaimsPrincipalFactory.SubClaim</c> из
/// <c>context.User</c>, а <c>sub</c> появляется только после <c>UseAuthentication</c>. Если
/// поставить <c>UseRateLimiter</c> выше аутентификации, ключом станет <c>ip:</c> или
/// <c>unknown</c>, и квота 30/мин на пользователя (R-22) станет общей на адрес: два клиента за
/// одним NAT начнут отбирать её друг у друга, а один клиент со сменой адреса обойдёт лимит.
/// <para>
/// Сценарий доказывает раздельные партиции на реальном конвейере: два пользователя (A и B)
/// регистрируются и входят <b>без</b> заголовка <c>Authorization</c> — это четыре записи в
/// анонимную партицию (<c>unknown</c>, потому что <c>TestServer</c> не заполняет
/// <c>RemoteIpAddress</c>), до лимита 30/мин далеко. Затем A отправляет ровно 30 запросов
/// <c>POST api/bookings</c> с пустым JSON-телом: пустое тело отсекает model binding, поэтому
/// каждый запрос — <c>400</c> (не <c>429</c>) и каждый расходует permit A. Тридцать первый
/// запрос A получает <c>429 rateLimit.exceeded</c>. Запрос B после этого — по-прежнему
/// <c>400</c>: у B своя партиция <c>sub</c>, чужая исчерпанная квота его не трогает.
/// </para>
/// <para>
/// Проверено red-green: с <c>app.UseRateLimiter()</c> выше <c>UseAuthentication</c>/
/// <c>UseAuthorization</c> тест падает (A получает <c>429</c> на 27-м запросе из 30, потому что
/// четыре анонимных запроса уже израсходовали общую партицию), с исходным порядком — зелёный.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RateLimitOrderTests
{
    private readonly PostgresFixture _database;

    public RateLimitOrderTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Write_limit_is_per_user_because_the_limiter_runs_after_authentication()
    {
        await using var app = await TestApp.StartAsync(_database);

        // Регистрация и вход — до установки Authorization, иначе они съели бы квоту A.
        var emailA = $"rate-limit-order-a-{Guid.NewGuid():N}@barbersearch.test";
        await app.RegisterAsync(emailA);
        var tokenA = await app.LoginAsync(emailA);

        var emailB = $"rate-limit-order-b-{Guid.NewGuid():N}@barbersearch.test";
        await app.RegisterAsync(emailB);
        var tokenB = await app.LoginAsync(emailB);

        app.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenA.AccessToken);

        // Пустое тело — 400 от model binding: запрос до обработчика не доходит, но permit
        // лимитера уже израсходован. Ровно WritePermitLimit запросов исчерпывают квоту A.
        for (var attempt = 1; attempt <= RateLimitSettings.WritePermitLimit; attempt++)
        {
            using var response = await app.Client.PostAsync("api/bookings", EmptyJsonBody());

            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadAsStringAsync();

                Assert.Fail(
                    $"Запрос {attempt} из {RateLimitSettings.WritePermitLimit} должен получить 400 "
                    + $"(binding пустого тела), получено {(int)response.StatusCode}. Тело: {body}");
            }
        }

        // Квота A исчерпана: тридцать первый запрос отклоняет лимитер, а не binding.
        using var rejected = await app.Client.PostAsync("api/bookings", EmptyJsonBody());
        var rejectedBody = await rejected.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("rateLimit.exceeded", rejectedBody, StringComparison.Ordinal);

        // Партиция B не тронута: тот же запрос проходит лимитер и получает 400, а не 429.
        app.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenB.AccessToken);

        using var fromUserB = await app.Client.PostAsync("api/bookings", EmptyJsonBody());
        var fromUserBBody = await fromUserB.Content.ReadAsStringAsync();

        Assert.True(
            fromUserB.StatusCode == HttpStatusCode.BadRequest,
            "Запрос пользователя B должен получить 400: его партиция sub не израсходована "
            + $"лимитом A, получено {(int)fromUserB.StatusCode}. Тело: {fromUserBBody}");
    }

    /// <summary>
    /// Пустое JSON-тело: обязательно для <c>[FromBody]</c>, но не проходит model binding.
    /// </summary>
    private static StringContent EmptyJsonBody() =>
        new(string.Empty, Encoding.UTF8, "application/json");
}
