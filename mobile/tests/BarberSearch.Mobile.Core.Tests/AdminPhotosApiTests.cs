using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Photos;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиентов очередей фото: два чтения, четыре решения, превью и разбор ошибок.
/// </summary>
/// <remarks>
/// <para>
/// Курсор непрозрачен: клиент не разбирает его, но обязан вернуть без потерь, поэтому
/// адрес проверяется после декодирования. Решения отвечают <c>204</c> без тела — клиент
/// не должен читать у него JSON, иначе успешное действие выглядело бы сбоем.
/// </para>
/// <para>
/// Превью читаются отдельным клиентом по абсолютному адресу из ответа API: вариант размера
/// уже в адресе, а <c>404</c> на неодобренный кадр обязан стать исключением, по которому
/// карточка скажет «не удалось показать», а не уронит экран.
/// </para>
/// <para>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест проходит через настоящий
/// <see cref="HttpClient"/> и настоящие настройки JSON, но не поднимает бэкенд.
/// </para>
/// </remarks>
public sealed class AdminPhotosApiTests
{
    private static readonly Guid UserId = Guid.Parse("9e100000-0000-4000-8000-0000000000a1");
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-0000000000b1");
    private static readonly Guid PhotoId = Guid.Parse("9e100000-0000-4000-8000-0000000000b2");

    [Fact]
    public async Task GetAvatarsAsync_FirstPage_SendsPageSizeWithoutCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, AvatarPageJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        await api.GetAvatarsAsync(cursor: null, pageSize: 20);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/admin/photos/avatars", handler.LastRequestUri!.AbsolutePath);

        // Пустой курсор не передаётся: сервер трактует отсутствие параметра как первую
        // страницу, а пустая строка была бы невалидным курсором.
        Assert.Equal("?pageSize=20", handler.LastRequestUri.Query);
        Assert.DoesNotContain("cursor=", handler.LastRequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAvatarsAsync_CursorWithReservedCharacters_IsUrlEscaped()
    {
        const string cursor = "abc+/=def";

        var handler = new StubHandler(HttpStatusCode.OK, AvatarPageJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        await api.GetAvatarsAsync(cursor, pageSize: 20);

        var query = handler.LastRequestUri!.Query;
        var cursorParameter = query
            .Split('&')
            .Single(part => part.StartsWith("cursor=", StringComparison.Ordinal));
        var escaped = cursorParameter["cursor=".Length..];

        Assert.Equal("abc%2B%2F%3Ddef", escaped);
        Assert.Equal(cursor, WebUtility.UrlDecode(escaped));
    }

    [Fact]
    public async Task GetAvatarsAsync_Item_ParsesOwnerAndStatus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, AvatarPageJson("\"next-page\""));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        var page = await api.GetAvatarsAsync(cursor: null, pageSize: 20);

        var item = Assert.Single(page.Items);

        Assert.Equal(UserId, item.UserId);
        Assert.Equal("Артём", item.FirstName);
        Assert.Equal("Соколов", item.LastName);
        Assert.Equal("artem@example.com", item.Email);
        Assert.Equal("/api/media/avatars/artem.jpg", item.AvatarUrl);
        Assert.Equal("Pending", item.ModerationStatus.ToString());
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), item.SubmittedAt);
        Assert.Equal("next-page", page.NextCursor);
    }

    [Fact]
    public async Task GetPortfolioAsync_Item_ParsesOwnerAndDescription()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PortfolioPageJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        var page = await api.GetPortfolioAsync(cursor: null, pageSize: 20);

        var item = Assert.Single(page.Items);

        Assert.Equal(PhotoId, item.PhotoId);
        Assert.Equal(BarberProfileId, item.BarberProfileId);
        Assert.Equal("Артём Соколов", item.BarberDisplayName);
        Assert.Equal("owner@example.com", item.OwnerEmail);
        Assert.Equal("/api/media/portfolio/work.jpg", item.Url);
        Assert.Equal("Стрижка машинкой", item.Description);
        Assert.True(item.IsPublished);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetPortfolioAsync_SecondPage_SendsCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PortfolioPageJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        await api.GetPortfolioAsync(cursor: "next-page", pageSize: 20);

        Assert.Equal("/api/admin/photos/portfolio", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal("?pageSize=20&cursor=next-page", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task ApproveAvatarAsync_SendsPostWithoutBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        await api.ApproveAvatarAsync(UserId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            $"/api/admin/users/{UserId:D}/avatar/approval",
            handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task RejectAvatarAsync_SendsReasonInBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        await api.RejectAvatarAsync(UserId, "Размытое фото");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            $"/api/admin/users/{UserId:D}/avatar/rejection",
            handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);

        Assert.Equal("Размытое фото", document.RootElement.GetProperty("reason").GetString());

        var parsed = JsonSerializer.Deserialize<RejectPhotoRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Размытое фото", parsed.Reason);
    }

    [Fact]
    public async Task ApprovePortfolioPhotoAsync_SendsPostToProfilePhotoPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        await api.ApprovePortfolioPhotoAsync(BarberProfileId, PhotoId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            $"/api/barber-profiles/{BarberProfileId:D}/photos/{PhotoId:D}/approval",
            handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task RejectPortfolioPhotoAsync_SendsReasonInBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        await api.RejectPortfolioPhotoAsync(BarberProfileId, PhotoId, "Не работа мастера");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            $"/api/barber-profiles/{BarberProfileId:D}/photos/{PhotoId:D}/rejection",
            handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);

        Assert.Equal(
            "Не работа мастера",
            document.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ApproveAvatarAsync_Conflict_ThrowsWithDomainCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The avatar has already been reviewed.",
              "errorCodes": ["user.avatarModeration.invalidStatus"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.ApproveAvatarAsync(UserId));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains(
            "user.avatarModeration.invalidStatus",
            exception.ErrorCodes,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAvatarsAsync_BrokenCursor_ThrowsWithCode()
    {
        const string problemJson = """
            {
              "title": "Bad Request",
              "status": 400,
              "errorCodes": ["admin.photos.cursor"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminPhotosApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetAvatarsAsync("broken", pageSize: 20));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("admin.photos.cursor", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetBytesAsync_AbsoluteUrl_SendsGetAndReturnsBytes()
    {
        byte[] expected = [0xFF, 0xD8, 0xFF, 0xE0];
        var handler = new StubHandler(HttpStatusCode.OK, bytes: expected);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMediaApi(httpClient);

        const string url = "http://localhost:5243/api/media/avatars/artem.jpg?size=md";

        var bytes = await api.GetBytesAsync(url);

        // Адрес уходит абсолютным и целиком: query с размером — часть адреса, её выбирает вызывающий.
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal(url, handler.LastRequestUri!.AbsoluteUri);
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task GetBytesAsync_NotFound_ThrowsBarberSearchApiException()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "detail": "The photo is not available.",
              "errorCodes": ["media.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMediaApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetBytesAsync("http://localhost:5243/api/media/avatars/pending.jpg?size=md"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("media.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    private static string AvatarPageJson(string nextCursor) => $$"""
        {
          "items": [
            {
              "userId": "{{UserId:D}}",
              "firstName": "Артём",
              "lastName": "Соколов",
              "email": "artem@example.com",
              "avatarUrl": "/api/media/avatars/artem.jpg",
              "moderationStatus": "Pending",
              "submittedAt": "2026-11-20T10:00:00+00:00"
            }
          ],
          "nextCursor": {{nextCursor}}
        }
        """;

    private static string PortfolioPageJson(string nextCursor) => $$"""
        {
          "items": [
            {
              "photoId": "{{PhotoId:D}}",
              "barberProfileId": "{{BarberProfileId:D}}",
              "barberDisplayName": "Артём Соколов",
              "ownerEmail": "owner@example.com",
              "url": "/api/media/portfolio/work.jpg",
              "description": "Стрижка машинкой",
              "isPublished": true,
              "moderationStatus": "Pending",
              "submittedAt": "2026-11-20T10:00:00+00:00"
            }
          ],
          "nextCursor": {{nextCursor}}
        }
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    /// <remarks>
    /// Тело бывает строкой JSON или готовыми байтами картинки: превью проверяется именно
    /// байтами, а не разобранной строкой.
    /// </remarks>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json",
        byte[]? bytes = null) : HttpMessageHandler
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

            HttpContent? content = bytes is not null
                ? new ByteArrayContent(bytes)
                : body is null
                    ? null
                    : new StringContent(body, Encoding.UTF8, contentType);

            return new HttpResponseMessage(statusCode)
            {
                Content = content,
            };
        }
    }
}
