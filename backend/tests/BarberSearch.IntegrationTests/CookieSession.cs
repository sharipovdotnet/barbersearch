namespace BarberSearch.IntegrationTests;

/// <summary>
/// Браузерный клиент теста: cookie-контейнер плюс antiforgery-токен текущей личности.
/// </summary>
/// <remarks>
/// Сессия создаётся <see cref="TestApp.SignInWithCookieAsync"/> и живёт отдельно от
/// <see cref="TestApp.Client"/>: Bearer-клиент и cookie-клиент должны иметь разные cookie
/// и разные заголовки, иначе тесты «Bearer не платит за CSRF» перестают что-либо доказывать.
/// </remarks>
public sealed class CookieSession : IAsyncDisposable
{
    /// <summary>
    /// Создаёт сессию из готового клиента и токена.
    /// </summary>
    public CookieSession(HttpClient client, string csrfToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(csrfToken);

        Client = client;
        CsrfToken = csrfToken;
    }

    /// <summary>
    /// Клиент с cookie-контейнером сессии; базовый адрес — <c>https://localhost</c>.
    /// </summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Свежий токен, привязанный к вошедшей личности.
    /// </summary>
    public string CsrfToken { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Client.Dispose();

        return ValueTask.CompletedTask;
    }
}
