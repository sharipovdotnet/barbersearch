using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.ChangePassword;
using BarberSearch.Contracts.Auth.EmailChange;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Contracts.Users.UpdateUserProfile;

namespace BarberSearch.Mobile.Core.Api.Account;

/// <summary>
/// HTTP-клиент аккаунта: сессия, профиль, смена пароля и заявка на смену email.
/// </summary>
/// <remarks>
/// <para>
/// Ответ <c>GET api/auth/me</c> строится сервером из claims, а не из базы: он показывает
/// ровно то, что видит сервер в текущем токене. Роль и подтверждение email ничего не
/// кэшируют — после смены роли старый токен отдаёт старое значение до истечения срока.
/// <c>GET api/users/me</c> отвечает из базы и поэтому актуален всегда; экран профиля читает
/// именно его.
/// </para>
/// <para>
/// <c>204</c> тело не читает: успех — это отсутствие ошибки. Тело запроса собирается из
/// контрактов бэкенда, чтобы расхождение ломало сборку, а не отправку.
/// </para>
/// </remarks>
public sealed class AccountApi : IAccountApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AccountApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync("api/auth/me", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var me = await response.Content
            .ReadFromJsonAsync<MeResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return me ?? throw new InvalidOperationException("The API returned an empty current-user response.");
    }

    /// <inheritdoc />
    public async Task<MyProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync("api/users/me", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var profile = await response.Content
            .ReadFromJsonAsync<MyProfileResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return profile ?? throw new InvalidOperationException("The API returned an empty profile response.");
    }

    /// <inheritdoc />
    public async Task UpdateProfileAsync(
        Guid userId,
        string firstName,
        string lastName,
        string? phoneNumber,
        string? avatarUrl,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateUserProfileRequest(firstName, lastName, phoneNumber, avatarUrl);

        using var response = await _httpClient
            .PutAsJsonAsync(BuildProfilePath(userId), request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var request = new ChangePasswordRequest(currentPassword, newPassword);

        using var response = await _httpClient
            .PostAsJsonAsync("api/auth/change-password", request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RequestEmailChangeAsync(
        string newEmail,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        var request = new RequestEmailChangeRequest(newEmail, currentPassword);

        using var response = await _httpClient
            .PostAsJsonAsync("api/auth/email-change", request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RequestEmailVerificationAsync(CancellationToken cancellationToken = default)
    {
        // Тела нет: адрес сервер берёт из токена, клиенту передавать нечего.
        using var response = await _httpClient
            .PostAsync("api/auth/email-verification", content: null, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Собирает адрес сохранения профиля.
    /// </summary>
    /// <remarks>
    /// Идентификатор форматируется инвариантно: он приходит из ответа <c>GET users/me</c>,
    /// а не собирается вручную, но формат <c>D</c> делает адрес одинаковым на любой культуре.
    /// </remarks>
    private static string BuildProfilePath(Guid userId) => string.Concat(
        "api/users/",
        userId.ToString("D", CultureInfo.InvariantCulture),
        "/profile");

    /// <summary>
    /// Бросает разобранную ошибку, если ответ неуспешный.
    /// </summary>
    /// <remarks>
    /// Успешные ответы здесь только <c>204</c>: тело не читается ни у одного из запросов,
    /// поэтому общая проверка статуса — единственное, что нужно.
    /// </remarks>
    private static async Task ThrowIfFailedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await BarberSearchApiException
            .FromResponseAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }
}
