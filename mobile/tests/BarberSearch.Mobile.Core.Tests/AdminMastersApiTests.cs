using System.Net;
using System.Text;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента админской очереди мастеров: фильтр, курсор, карточка и записи подтверждения.
/// </summary>
/// <remarks>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест не поднимает бэкенд, но проходит
/// через настоящий <see cref="HttpClient"/> и настоящие настройки JSON. Записи проверяются
/// по методу и адресу: тело у <c>204</c> не читается, а причина снятия подтверждения серверу
/// не передаётся.
/// </remarks>
public sealed class AdminMastersApiTests
{
    private static readonly Guid MasterId = Guid.Parse("9e100000-0000-4000-8000-0000000000d1");
    private static readonly Guid UserId = Guid.Parse("9e100000-0000-4000-8000-0000000000d2");
    private static readonly Guid PhotoId = Guid.Parse("9e100000-0000-4000-8000-0000000000d3");

    [Fact]
    public async Task GetMastersAsync_PendingFirstPage_SendsStatusAndPageSizeWithoutCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MastersJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        await api.GetMastersAsync(AdminMasterFilter.Pending, cursor: null, pageSize: 20);

        var uri = handler.LastRequestUri!;

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/admin/barbers", uri.AbsolutePath);

        // Отсутствие курсора — это первая страница; пустая строка была бы невалидным курсором.
        Assert.Equal("?status=pending&pageSize=20", uri.Query);
        Assert.DoesNotContain("cursor=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMastersAsync_HiddenWithCursor_IsUrlEscaped()
    {
        const string cursor = "abc+/=def";

        var handler = new StubHandler(HttpStatusCode.OK, MastersJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        await api.GetMastersAsync(AdminMasterFilter.Hidden, cursor, pageSize: 10);

        var uri = handler.LastRequestUri!;

        Assert.Equal("?status=hidden&pageSize=10&cursor=abc%2B%2F%3Ddef", uri.Query);
    }

    [Fact]
    public async Task GetMastersAsync_AllFilter_SendsAll()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MastersJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        await api.GetMastersAsync(AdminMasterFilter.All, cursor: null, pageSize: 20);

        Assert.Equal("?status=all&pageSize=20", handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task GetMastersAsync_Response_ParsesRowAndCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MastersJson("\"next-page\""));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        var page = await api.GetMastersAsync(AdminMasterFilter.Pending, cursor: null, pageSize: 20);

        var item = Assert.Single(page.Items);

        Assert.Equal(MasterId, item.Id);
        Assert.Equal("Иван Мастер", item.DisplayName);
        Assert.Equal("ivan@example.com", item.Email);
        Assert.Equal(4.8m, item.Rating);
        Assert.Equal(12, item.ReviewsCount);
        Assert.True(item.IsActive);
        Assert.False(item.IsVerified);
        Assert.Null(item.AvatarUrl);
        Assert.False(item.IsAvatarBlocked);
        Assert.Equal("Approved", item.AvatarModerationStatus.ToString());

        // Ненулевой курсор — единственный признак, что очередь можно догрузить.
        Assert.Equal("next-page", page.NextCursor);
    }

    [Fact]
    public async Task GetMastersAsync_Forbidden_ThrowsWithStatus()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "Only an administrator can read this queue.",
              "errorCodes": ["auth.forbidden"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Forbidden, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetMastersAsync(AdminMasterFilter.Pending, cursor: null, pageSize: 20));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("auth.forbidden", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetMasterAsync_ById_ParsesDetailReadinessAndPhotos()
    {
        var handler = new StubHandler(HttpStatusCode.OK, DetailJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        var detail = await api.GetMasterAsync(MasterId);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal($"/api/admin/barbers/{MasterId:D}", handler.LastRequestUri!.AbsolutePath);

        Assert.Equal(MasterId, detail.Id);
        Assert.Equal(UserId, detail.UserId);
        Assert.Equal("Иван Мастер", detail.DisplayName);
        Assert.Equal("+7 900 000-00-00", detail.PhoneNumber);
        Assert.Equal(9, detail.YearsOfExperience);
        Assert.False(detail.IsVerified);
        Assert.True(detail.IsActive);

        // Готовность — счётчики, а не списки: карточка телефона показывает чек-лист числами.
        Assert.True(detail.Readiness.IsProfileFilled);
        Assert.False(detail.Readiness.IsEmailVerified);
        Assert.Equal(3, detail.Readiness.ServicesCount);
        Assert.Equal(5, detail.Readiness.ScheduleDaysCount);

        var photo = Assert.Single(detail.Photos);

        Assert.Equal(PhotoId, photo.Id);
        Assert.Equal("Стрижка", photo.Description);
        Assert.Equal("Pending", photo.ModerationStatus.ToString());
    }

    [Fact]
    public async Task GetMasterAsync_NotFound_ThrowsWithStatus()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "errorCodes": ["barberProfile.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetMasterAsync(MasterId));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("barberProfile.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task VerifyMasterAsync_SendsPostWithoutBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        await api.VerifyMasterAsync(MasterId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal($"/api/barber-profiles/{MasterId:D}/verification", handler.LastRequestUri!.AbsolutePath);

        // 204 без тела — успех: клиент не читает содержимое и не отправляет своё.
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task UnverifyMasterAsync_SendsDelete()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        await api.UnverifyMasterAsync(MasterId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal($"/api/barber-profiles/{MasterId:D}/verification", handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task VerifyMasterAsync_InactiveProfile_ThrowsWithDomainCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "An inactive barber profile cannot be verified.",
              "errorCodes": ["barberProfile.verify.inactive"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.VerifyMasterAsync(MasterId));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("barberProfile.verify.inactive", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetMastersAsync_EmptyBody_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMastersApi(httpClient);

        // null вместо страницы — нарушение контракта: пустой список не должен выглядеть
        // как конец очереди.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.GetMastersAsync(AdminMasterFilter.Pending, cursor: null, pageSize: 20));
    }

    [Fact]
    public async Task GetBytesAsync_AbsoluteUrl_ReadsFileFromThatUrl()
    {
        const string payload = "avatar-bytes";
        var handler = new StubHandler(HttpStatusCode.OK, payload, "image/webp");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMediaApi(httpClient);

        var bytes = await api.GetBytesAsync(
            "http://localhost:5243/api/media/avatars/ivan.webp?size=md");

        Assert.Equal(HttpMethod.Get, handler.LastMethod);

        // Адрес берётся целиком: вариант размера уже часть адреса, а не запрос клиента.
        Assert.Equal(
            "http://localhost:5243/api/media/avatars/ivan.webp?size=md",
            handler.LastRequestUri!.AbsoluteUri);
        Assert.Equal(payload, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task GetBytesAsync_PortfolioNotFound_ThrowsWithStatusAndCode()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "detail": "The portfolio photo file was not found.",
              "errorCodes": ["portfolioMedia.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminMediaApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetBytesAsync("http://localhost:5243/api/media/portfolio/work.webp?size=md"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("portfolioMedia.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    private static string MastersJson(string nextCursor) => $$"""
        {
          "items": [
            {
              "id": "{{MasterId:D}}",
              "displayName": "Иван Мастер",
              "email": "ivan@example.com",
              "grade": "MiddleBarber",
              "rating": 4.8,
              "reviewsCount": 12,
              "isActive": true,
              "isVerified": false,
              "createdAt": "2026-11-01T10:00:00+00:00",
              "updatedAt": "2026-11-15T12:30:00+00:00",
              "avatarUrl": null,
              "isAvatarBlocked": false,
              "avatarModerationStatus": "Approved"
            }
          ],
          "nextCursor": {{nextCursor}}
        }
        """;

    private static string DetailJson() => $$"""
        {
          "id": "{{MasterId:D}}",
          "userId": "{{UserId:D}}",
          "displayName": "Иван Мастер",
          "email": "ivan@example.com",
          "phoneNumber": "+7 900 000-00-00",
          "avatarUrl": "/api/media/avatars/ivan.webp",
          "grade": "MiddleBarber",
          "bio": "Стригу с 2015 года.",
          "yearsOfExperience": 9,
          "rating": 4.8,
          "reviewsCount": 12,
          "isActive": true,
          "isVerified": false,
          "readiness": {
            "isProfileFilled": true,
            "isEmailVerified": false,
            "servicesCount": 3,
            "salonsCount": 1,
            "scheduleDaysCount": 5,
            "photosCount": 1
          },
          "createdAt": "2026-11-01T10:00:00+00:00",
          "updatedAt": "2026-11-15T12:30:00+00:00",
          "photos": [
            {
              "id": "{{PhotoId:D}}",
              "url": "/api/media/portfolio/work.webp",
              "thumbnailUrl": "/api/media/portfolio/work.webp?size=md",
              "description": "Стрижка",
              "sortOrder": 0,
              "isPublished": true,
              "isBlocked": false,
              "moderationStatus": "Pending",
              "rejectionReason": null
            }
          ],
          "isAvatarBlocked": false,
          "avatarModerationStatus": "Approved",
          "avatarRejectionReason": null
        }
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json") : HttpMessageHandler
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
