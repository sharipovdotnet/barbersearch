using System.Net;
using System.Text.Json;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Обязательность <c>barberProfileId</c> в <c>GET api/reviews</c> на реальном хосте:
/// отсутствие параметра отсекает model binding, пустой GUID — доменная проверка.
/// </summary>
/// <remarks>
/// До R-52 отсутствие параметра связывалось в <see cref="Guid.Empty"/> и доходило до
/// <c>GetReviewsHandler</c>, поэтому клиент получал доменный <c>review.getReviews.barberProfileId</c>
/// и не мог отличить «параметр не передан» от «передан пустой». <c>[BindRequired]</c> даёт
/// <c>400 ValidationProblemDetails</c> с ошибкой на поле <c>barberProfileId</c> ещё до
/// обработчика; явный <c>00000000-…</c> по-прежнему проверяется в домене (defense-in-depth).
/// Один сценарий проверяет все три состояния: нет параметра, пустой GUID, несуществующий профиль.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class ReviewsBindingTests
{
    private readonly PostgresFixture _database;

    public ReviewsBindingTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Barber_profile_id_is_required_and_empty_is_still_a_domain_validation_error()
    {
        await using var app = await TestApp.StartAsync(_database);

        // 1. Параметр не передан: запрос отсекает model binding, доменный код не появляется.
        using var missing = await app.Client.GetAsync("api/reviews");
        var missingBody = await missing.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using (var document = JsonDocument.Parse(missingBody))
        {
            Assert.True(
                document.RootElement.TryGetProperty("errors", out var errors),
                $"Binding failure must carry 'errors'; body: {missingBody}");
            Assert.True(
                errors.TryGetProperty("barberProfileId", out var messages),
                $"Binding failure must carry 'errors.barberProfileId'; body: {missingBody}");
            Assert.NotEqual(0, messages.GetArrayLength());
        }

        Assert.DoesNotContain("review.getReviews", missingBody, StringComparison.Ordinal);

        // 2. Явный пустой GUID: доменная validation-ошибка, а не binding (defense-in-depth).
        using var empty = await app.Client.GetAsync(
            $"api/reviews?barberProfileId={Guid.Empty}");
        var emptyBody = await empty.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("review.getReviews.barberProfileId", emptyBody, StringComparison.Ordinal);

        // 3. Несуществующий непустой профиль: пустая страница (TestApp стартует с чистой БД).
        using var page = await app.Client.GetAsync(
            $"api/reviews?barberProfileId={Guid.CreateVersion7()}");
        var pageBody = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        using var pageDocument = JsonDocument.Parse(pageBody);
        Assert.Equal(0, pageDocument.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, pageDocument.RootElement.GetProperty("nextCursor").ValueKind);
    }
}
