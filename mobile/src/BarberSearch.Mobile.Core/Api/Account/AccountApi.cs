using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.ChangePassword;
using BarberSearch.Contracts.Auth.EmailChange;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Contracts.Users.UpdateUserProfile;
using BarberSearch.Mobile.Core.Media;

namespace BarberSearch.Mobile.Core.Api.Account;

/// <summary>
/// HTTP-клиент аккаунта: сессия, профиль, аватар, смена пароля и заявка на смену email.
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
/// Аватар живёт в агрегате пользователя, а не в профиле барбера: <c>api/users/me/avatar</c>
/// загружает и удаляет кадр аккаунта, который у барбера показывается в открытой карточке.
/// Загрузка — <c>multipart/form-data</c> с одной частью <c>file</c>; сервер принимает её в
/// премодерацию, поэтому ответ <c>201</c> несёт ещё и статус проверки.
/// </para>
/// <para>
/// <c>204</c> тело не читает: успех — это отсутствие ошибки. Единственное исключение —
/// загрузка аватара: её <c>201</c> читается после проверки статуса. Тело запроса собирается
/// из контрактов бэкенда, чтобы расхождение ломало сборку, а не отправку.
/// </para>
/// </remarks>
public sealed class AccountApi : IAccountApi, IAccountAvatarApi
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
    public async Task<AvatarUploadResponse> UploadAvatarAsync(
        PortfolioImage image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(image.Bytes);

        // Имя части задаёт вызывающий код: сервер ищет файл по имени file, а MIME-тип
        // перепроверяет по сигнатуре, поэтому заголовок — подсказка, а не доверие.
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
        form.Add(fileContent, "file", image.FileName);

        using var response = await _httpClient
            .PostAsync("api/users/me/avatar", form, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);

        // 201 несёт адрес нового кадра и статус премодерации: без него экран не покажет
        // бейдж «На проверке», поэтому пустой ответ — ошибка клиента, а не успех.
        var uploaded = await response.Content
            .ReadFromJsonAsync<AvatarUploadResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return uploaded ?? throw new InvalidOperationException("The API returned an empty avatar response.");
    }

    /// <inheritdoc />
    public async Task DeleteAvatarAsync(CancellationToken cancellationToken = default)
    {
        // Идемпотентно: 204 приходит и когда аватара не было, тело не читается.
        using var response = await _httpClient
            .DeleteAsync("api/users/me/avatar", cancellationToken)
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
    /// Успех проверяется до чтения тела: <c>204</c> у большинства запросов и <c>201</c> у
    /// загрузки аватара. Единая проверка освобождает вызывающий код от повторов и не даёт
    /// прочитать ProblemDetails как успешный ответ.
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
