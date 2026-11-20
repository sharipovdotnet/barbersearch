using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// HTTP-клиент инбокса напоминаний и настроек мастера.
/// </summary>
/// <remarks>
/// Список курсорный, счётчик непрочитанных — отдельное дешёвое чтение, настройки — один
/// <c>GET</c> и одна <c>PUT</c> на изменение. Действия без тела отвечают <c>204</c>, и их
/// тело не читается; настройки и код привязки — <c>200</c> с JSON.
/// <c>X-CSRF-TOKEN</c> не отправляется: клиент Bearer, а не cookie.
/// </remarks>
public sealed class BarberNotificationsApi : IBarberNotificationsApi
{
    private const string NotificationsPath = "api/barber/notifications";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// Клиент с заданным <see cref="HttpClient.BaseAddress"/> и обработчиком авторизации.
    /// </param>
    public BarberNotificationsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<BarberNotificationResponse>> GetAsync(
        string? cursor = null,
        int? pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var path = BuildNotificationsPath(cursor, pageSize);

        using var response = await _httpClient
            .GetAsync(path, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BarberNotificationResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException(
            "The API returned an empty barber notifications response.");
    }

    /// <inheritdoc />
    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The notification identifier must not be empty.", nameof(id));
        }

        await PostWithoutBodyAsync(
                string.Concat(
                    NotificationsPath,
                    "/",
                    id.ToString("D", CultureInfo.InvariantCulture),
                    "/read"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        await PostWithoutBodyAsync(
                string.Concat(NotificationsPath, "/read-all"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(string.Concat(NotificationsPath, "/unread-count"), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var unread = await response.Content
            .ReadFromJsonAsync<UnreadNotificationsCountResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return unread is null
            ? throw new InvalidOperationException("The API returned an empty unread count response.")
            : unread.Count;
    }

    /// <inheritdoc />
    public async Task<NotificationSettingsResponse> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync("api/barber/notification-settings", cancellationToken)
            .ConfigureAwait(false);

        return await ReadSettingsAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<NotificationSettingsResponse> UpdateSettingsAsync(
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "api/barber/notification-settings")
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        return await ReadSettingsAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TelegramLinkResponse> CreateTelegramLinkAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsync(
                string.Concat(NotificationsPath, "/telegram/link"),
                content: null,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var link = await response.Content
            .ReadFromJsonAsync<TelegramLinkResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return link ?? throw new InvalidOperationException(
            "The API returned an empty Telegram link response.");
    }

    /// <inheritdoc />
    public async Task UnlinkTelegramAsync(CancellationToken cancellationToken = default)
    {
        await PostWithoutBodyAsync(
                string.Concat(NotificationsPath, "/telegram/unlink"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Читает настройки из успешного ответа и проверяет, что тело не потеряно.
    /// </summary>
    /// <remarks>
    /// <c>PUT</c> и <c>GET</c> отвечают одной формой, поэтому проверка успеха и разбор тела
    /// вынесены в один метод: иначе ошибку в одном из двух путей было бы легко пропустить.
    /// </remarks>
    private static async Task<NotificationSettingsResponse> ReadSettingsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var settings = await response.Content
            .ReadFromJsonAsync<NotificationSettingsResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return settings ?? throw new InvalidOperationException(
            "The API returned an empty notification settings response.");
    }

    /// <summary>
    /// Отправляет действие без тела; тело успешного <c>204</c> не читается.
    /// </summary>
    private async Task PostWithoutBodyAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsync(path, content: null, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Собирает адрес страницы инбокса.
    /// </summary>
    /// <remarks>
    /// Пустой курсор не передаётся: отсутствие параметра и есть первая страница, а
    /// <c>cursor=</c> вызвал бы ошибку разбора. Курсор экранируется целиком — он непрозрачен.
    /// </remarks>
    private static string BuildNotificationsPath(string? cursor, int? pageSize)
    {
        var parameters = new List<string>(2);

        if (pageSize is not null)
        {
            parameters.Add(string.Concat(
                "pageSize=",
                pageSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            parameters.Add(string.Concat("cursor=", Uri.EscapeDataString(cursor)));
        }

        return parameters.Count == 0
            ? NotificationsPath
            : string.Concat(NotificationsPath, "?", string.Join('&', parameters));
    }

    /// <summary>
    /// Бросает <see cref="BarberSearchApiException"/>, если ответ неуспешный.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
