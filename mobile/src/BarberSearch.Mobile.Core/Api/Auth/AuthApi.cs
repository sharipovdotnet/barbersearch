using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Logout;
using BarberSearch.Contracts.Auth.PasswordReset;
using BarberSearch.Contracts.Auth.Refresh;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Auth;

/// <summary>
/// HTTP-клиент авторизации: регистрация, вход, запрос ссылки сброса, refresh и выход.
/// </summary>
/// <remarks>
/// Клиент тонкий, как <see cref="BarberSearchApi"/>: адрес, сериализация и разбор ошибок.
/// Признак успешного входа — непустой <c>refreshToken</c> в теле; его наличие проверяет
/// <see cref="BarberSearch.Mobile.Core.Auth.TokenSession"/>, который и владеет парой.
/// </remarks>
public sealed class AuthApi : IAuthApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AuthApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<Guid> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient
            .PostAsJsonAsync("api/auth/register", request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var created = await response.Content
            .ReadFromJsonAsync<CreatedResourceResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return created?.Id
            ?? throw new InvalidOperationException("The API returned an empty registration response.");
    }

    /// <inheritdoc />
    public async Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync(
                "api/auth/password-reset",
                new RequestPasswordResetRequest(email),
                ApiJson.Options,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<TokenResponse> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync("api/auth/login", new LoginRequest(email, password), ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return await ReadTokensAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TokenResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync("api/auth/refresh", new RefreshTokenRequest(refreshToken), ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return await ReadTokensAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync("api/auth/logout", new LogoutRequest(refreshToken), ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Проверяет код ответа и разбирает пару токенов.
    /// </summary>
    /// <remarks>
    /// Пустое тело — нарушение контракта, а не состояние сессии: сервер обязан прислать пару.
    /// Проверять <c>refreshToken</c> здесь незачем — это инвариант <c>TokenSession</c>.
    /// </remarks>
    private static async Task<TokenResponse> ReadTokensAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var tokens = await response.Content
            .ReadFromJsonAsync<TokenResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return tokens ?? throw new InvalidOperationException("The API returned an empty token response.");
    }
}
