using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Photos;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// HTTP-клиент очередей премодерации фотографий.
/// </summary>
/// <remarks>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: роль администратора сервер берёт
/// из токена, в запросе её нет. Ответы читаются контрактами бэкенда, поэтому расхождение
/// путей или полей ломает сборку, а не экран.
/// </remarks>
public sealed class AdminPhotosApi : IAdminPhotosApi
{
    private const string AvatarsPath = "api/admin/photos/avatars";

    private const string PortfolioPath = "api/admin/photos/portfolio";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AdminPhotosApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<AdminAvatarPhotoResponse>> GetAvatarsAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildPagePath(AvatarsPath, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminAvatarPhotoResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty avatar photo response.");
    }

    /// <inheritdoc />
    public async Task<PagedResponse<AdminPortfolioPhotoResponse>> GetPortfolioAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildPagePath(PortfolioPath, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminPortfolioPhotoResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty portfolio photo response.");
    }

    /// <inheritdoc />
    public async Task ApproveAvatarAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsync(BuildAvatarDecisionPath(userId, "approval"), content: null, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RejectAvatarAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var request = new RejectPhotoRequest(reason);

        using var response = await _httpClient
            .PostAsJsonAsync(
                BuildAvatarDecisionPath(userId, "rejection"),
                request,
                ApiJson.Options,
                cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApprovePortfolioPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsync(BuildPortfolioDecisionPath(barberProfileId, photoId, "approval"), content: null, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RejectPortfolioPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var request = new RejectPhotoRequest(reason);

        using var response = await _httpClient
            .PostAsJsonAsync(
                BuildPortfolioDecisionPath(barberProfileId, photoId, "rejection"),
                request,
                ApiJson.Options,
                cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Собирает адрес страницы очереди.
    /// </summary>
    /// <remarks>
    /// Пустой курсор не передаётся вовсе: сервер трактует отсутствие параметра как первую
    /// страницу, а пустая строка была бы невалидным курсором. Курсор непрозрачен и может
    /// содержать символы, недопустимые в адресе, поэтому кодируется целиком.
    /// </remarks>
    /// <param name="path">Путь очереди.</param>
    /// <param name="cursor">Курсор следующей страницы либо <c>null</c>.</param>
    /// <param name="pageSize">Размер страницы.</param>
    /// <returns>Относительный адрес запроса.</returns>
    private static string BuildPagePath(string path, string? cursor, int pageSize)
    {
        var query = string.Concat(
            path,
            "?pageSize=",
            pageSize.ToString(CultureInfo.InvariantCulture));

        return string.IsNullOrWhiteSpace(cursor)
            ? query
            : string.Concat(query, "&cursor=", Uri.EscapeDataString(cursor));
    }

    /// <summary>
    /// Собирает адрес решения по аватару.
    /// </summary>
    /// <param name="userId">Пользователь-владелец аватара.</param>
    /// <param name="action">Действие: <c>approval</c> или <c>rejection</c>.</param>
    /// <returns>Относительный адрес запроса.</returns>
    private static string BuildAvatarDecisionPath(Guid userId, string action) => string.Concat(
        "api/admin/users/",
        userId.ToString("D", CultureInfo.InvariantCulture),
        "/avatar/",
        action);

    /// <summary>
    /// Собирает адрес решения по работе портфолио.
    /// </summary>
    /// <param name="barberProfileId">Профиль, которому принадлежит фотография.</param>
    /// <param name="photoId">Фотография.</param>
    /// <param name="action">Действие: <c>approval</c> или <c>rejection</c>.</param>
    /// <returns>Относительный адрес запроса.</returns>
    private static string BuildPortfolioDecisionPath(
        Guid barberProfileId,
        Guid photoId,
        string action) => string.Concat(
            "api/barber-profiles/",
            barberProfileId.ToString("D", CultureInfo.InvariantCulture),
            "/photos/",
            photoId.ToString("D", CultureInfo.InvariantCulture),
            "/",
            action);

    /// <summary>
    /// Превращает неуспешный ответ в исключение API.
    /// </summary>
    /// <remarks>
    /// Успех — это <c>204</c>: тело не читается, потому что решения ничего не возвращают.
    /// </remarks>
    /// <param name="response">Ответ сервера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача проверки.</returns>
    /// <exception cref="BarberSearchApiException">Ответ неуспешен.</exception>
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
