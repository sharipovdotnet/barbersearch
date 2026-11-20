using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;
using BarberSearch.Domain.Notifications;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента инбокса напоминаний, настроек и привязки Telegram.
/// </summary>
/// <remarks>
/// Действия без тела (<c>read</c>, <c>read-all</c>, <c>unlink</c>) отвечают <c>204</c>,
/// настройки и код привязки — <c>200</c> с JSON. Счётчик непрочитанных разбирается в число,
/// потому что экрану нужен только <c>count</c>, а не весь объект.
/// </remarks>
public sealed class BarberNotificationsApiTests
{
    private static readonly Guid NotificationId = Guid.Parse("9e100000-0000-4000-8000-0000000000e1");
    private static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000b1");

    [Fact]
    public async Task GetAsync_CursorAndPageSize_SendsQueryAndParsesPage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PageJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var page = await api.GetAsync("next-page", pageSize: 10);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/barber/notifications", handler.LastRequestUri!.AbsolutePath);

        var query = QueryParameters(handler.LastRequestUri);

        Assert.Equal("next-page", query["cursor"]);
        Assert.Equal("10", query["pageSize"]);

        var notification = Assert.Single(page.Items);

        Assert.Equal(NotificationId, notification.Id);
        Assert.Equal(BarberNotificationKind.Upcoming, notification.Kind);
        Assert.Equal(BarberNotificationDelivery.Sent, notification.Delivery);
        Assert.Equal(BookingId, notification.BookingId);
        Assert.Null(notification.ReadAt);
        Assert.Equal("Europe/Moscow", notification.SalonTimeZoneId);
        Assert.Null(notification.BookingsCount);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetAsync_NoCursor_OmitsCursorAndKeepsPageSize()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"items":[],"nextCursor":null}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        await api.GetAsync();

        var query = QueryParameters(handler.LastRequestUri!);

        // Первая страница: курсора нет вовсе, а пустая строка была бы невалидным курсором.
        Assert.False(query.ContainsKey("cursor"));
        Assert.Equal("20", query["pageSize"]);
    }

    [Fact]
    public async Task GetAsync_NullJsonBody_ThrowsInsteadOfReturningEmptyPage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAsync());
    }

    [Fact]
    public async Task MarkReadAsync_ValidId_PostsReadPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        await api.MarkReadAsync(NotificationId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/barber/notifications/", NotificationId.ToString("D"), "/read"),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task MarkReadAsync_NotFound_ThrowsWithStatusCode()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "detail": "The notification was not found.",
              "errorCodes": ["notification.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.MarkReadAsync(NotificationId));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("notification.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task MarkAllReadAsync_PostsReadAllPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        await api.MarkAllReadAsync();

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/barber/notifications/read-all", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetUnreadCountAsync_Count_ReturnsNumber()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"count":7}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var count = await api.GetUnreadCountAsync();

        Assert.Equal(7, count);
        Assert.Equal("/api/barber/notifications/unread-count", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetUnreadCountAsync_NullBody_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetUnreadCountAsync());
    }

    [Fact]
    public async Task GetSettingsAsync_Settings_Deserialises()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SettingsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var settings = await api.GetSettingsAsync();

        Assert.Equal("/api/barber/notification-settings", handler.LastRequestUri!.AbsolutePath);
        Assert.True(settings.IsEnabled);
        Assert.True(settings.DigestEnabled);

        // Формат HH:mm — контракт с полем времени, а не сериализация TimeOnly.
        Assert.Equal("08:00", settings.DigestTime);
        Assert.Equal(30, settings.LeadMinutes);
        Assert.False(settings.TelegramLinked);
        Assert.Null(settings.TelegramLinkedAt);
        Assert.True(settings.BotConfigured);
    }

    [Fact]
    public async Task UpdateSettingsAsync_Request_PutsBodyAndParsesNormalisedResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SettingsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var request = new UpdateNotificationSettingsRequest(
            IsEnabled: true,
            DigestEnabled: true,
            DigestTime: "08:00",
            LeadMinutes: 30);

        var settings = await api.UpdateSettingsAsync(request);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal("/api/barber/notification-settings", handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<UpdateNotificationSettingsRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsEnabled);
        Assert.True(parsed.DigestEnabled);
        Assert.Equal("08:00", parsed.DigestTime);
        Assert.Equal(30, parsed.LeadMinutes);

        // PUT отвечает 200 с полным телом: экран показывает нормализованные сервером значения.
        Assert.Equal("08:00", settings.DigestTime);
    }

    [Fact]
    public async Task UpdateSettingsAsync_RejectedField_ThrowsWithErrorCode()
    {
        const string problemJson = """
            {
              "title": "Bad Request",
              "status": 400,
              "detail": "The digest time is invalid.",
              "errorCodes": ["notificationSettings.digestTime"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.UpdateSettingsAsync(new UpdateNotificationSettingsRequest(
                true,
                true,
                "03:00",
                30)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains(
            "notificationSettings.digestTime",
            exception.ErrorCodes,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateTelegramLinkAsync_Link_DeserialisesCodeAndDeepLink()
    {
        var handler = new StubHandler(HttpStatusCode.OK, TelegramLinkJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var link = await api.CreateTelegramLinkAsync();

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/barber/notifications/telegram/link", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal("ABC123", link.Code);
        Assert.Equal("https://t.me/barbersearch_bot?start=ABC123", link.DeepLink);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 12, 10, 0, TimeSpan.Zero),
            link.ExpiresAt);
    }

    [Fact]
    public async Task CreateTelegramLinkAsync_AlreadyLinked_ThrowsWithStatusCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "Telegram is already linked.",
              "errorCodes": ["notification.telegram.alreadyLinked"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateTelegramLinkAsync());

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains(
            "notification.telegram.alreadyLinked",
            exception.ErrorCodes,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task UnlinkTelegramAsync_PostsUnlinkPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        await api.UnlinkTelegramAsync();

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/barber/notifications/telegram/unlink", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task MarkReadAsync_EmptyId_ThrowsWithoutRequest()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberNotificationsApi(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => api.MarkReadAsync(Guid.Empty));

        Assert.Null(handler.LastRequestUri);
    }

    private static string PageJson() => $$"""
        {
          "items": [
            {
              "id": "{{NotificationId:D}}",
              "kind": "Upcoming",
              "createdAt": "2026-11-20T09:00:00+00:00",
              "readAt": null,
              "bookingId": "{{BookingId:D}}",
              "startAt": "2026-11-20T17:30:00+00:00",
              "serviceName": "Стрижка",
              "salonName": "E2E Studio",
              "salonTimeZoneId": "Europe/Moscow",
              "bookingsCount": null,
              "delivery": "Sent"
            }
          ],
          "nextCursor": null
        }
        """;

    private static string SettingsJson() => """
        {
          "isEnabled": true,
          "digestEnabled": true,
          "digestTime": "08:00",
          "leadMinutes": 30,
          "telegramLinked": false,
          "telegramLinkedAt": null,
          "botConfigured": true
        }
        """;

    private static string TelegramLinkJson() => """
        {
          "code": "ABC123",
          "deepLink": "https://t.me/barbersearch_bot?start=ABC123",
          "expiresAt": "2026-11-20T12:10:00+00:00"
        }
        """;

    /// <summary>
    /// Разбирает строку запроса вручную: <see cref="Uri"/> раскрывает не все символы.
    /// </summary>
    private static Dictionary<string, string> QueryParameters(Uri uri)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];

            parameters[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value);
        }

        return parameters;
    }

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json")
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            };
        }
    }
}
