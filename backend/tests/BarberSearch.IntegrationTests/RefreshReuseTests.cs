using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Refresh;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Конвейер refresh-токенов на реальной таблице: ротация, обнаружение повторного использования
/// и отзыв всей цепочки.
/// </summary>
/// <remarks>
/// Проверяется не «форма ответа», а состояние <c>refresh_tokens</c> после ротации: повтор
/// старого токена обязан вернуть <c>401 auth.refresh.reused</c> и погасить замену, поэтому
/// новый токен из первого обмена тоже перестаёт работать (<c>auth.refresh.revoked</c>).
/// Это ровно та гонка/кража, которую unit-тест с фейком репозитория не воспроизводит.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RefreshReuseTests
{
    private readonly PostgresFixture _database;

    public RefreshReuseTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Replaying_a_rotated_refresh_token_revokes_the_whole_chain()
    {
        await using var app = await TestApp.StartAsync(_database);

        var email = $"refresh-{Guid.NewGuid():N}@barbersearch.test";
        await app.RegisterAsync(email);

        var login = await app.LoginAsync(email);
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));

        var originalRefresh = login.RefreshToken!;

        using var rotation = await app.Client.PostAsJsonAsync(
            "api/auth/refresh",
            new RefreshTokenRequest(originalRefresh));
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);

        var rotated = await rotation.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(rotated);
        Assert.False(string.IsNullOrWhiteSpace(rotated.RefreshToken));
        Assert.NotEqual(originalRefresh, rotated.RefreshToken);

        using var replay = await app.Client.PostAsJsonAsync(
            "api/auth/refresh",
            new RefreshTokenRequest(originalRefresh));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains("auth.refresh.reused", await replay.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Цепочка погашена: токен, выданный при ротации, тоже больше не принимается.
        using var afterReuse = await app.Client.PostAsJsonAsync(
            "api/auth/refresh",
            new RefreshTokenRequest(rotated.RefreshToken!));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
        Assert.Contains("auth.refresh.revoked", await afterReuse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
