using System.Net;
using System.Text;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента админских отзывов: очередь по статусу, карточка и запись публикации.
/// </summary>
/// <remarks>
/// <para>
/// Админское чтение отличается от клиентского и адресом, и контрактом: очередь живёт под
/// <c>api/admin/reviews</c>, а решение — это существующие ручки отзыва
/// <c>POST</c>/<c>DELETE api/reviews/{id}/publication</c>. Сеть подменяется обработчиком,
/// поэтому тест проходит через настоящий <see cref="HttpClient"/> и настоящие настройки JSON.
/// </para>
/// <para>
/// Курсор непрозрачен: клиент не разбирает его, но обязан вернуть без потерь, поэтому
/// адрес проверяется и как экранированная строка, и после декодирования. У записей тела нет:
/// успех — <c>204</c>, и клиент не должен читать у него JSON.
/// </para>
/// </remarks>
public sealed class AdminReviewsApiTests
{
    private static readonly Guid ReviewId = Guid.Parse("9e100000-0000-4000-8000-0000000000d1");
    private static readonly Guid BarberId = Guid.Parse("9e100000-0000-4000-8000-0000000000d2");
    private static readonly Guid CustomerId = Guid.Parse("9e100000-0000-4000-8000-0000000000d3");
    private static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000d4");

    [Fact]
    public async Task GetReviewsAsync_PendingFirstPage_SendsStatusAndPageSizeWithoutCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PageJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        await api.GetReviewsAsync(AdminReviewStatusFilter.Pending, cursor: null, pageSize: 20);

        var uri = handler.LastRequestUri!;

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/admin/reviews", uri.AbsolutePath);

        // Отсутствие курсора — это первая страница; пустая строка была бы невалидным курсором.
        Assert.Equal("?status=pending&pageSize=20", uri.Query);
        Assert.DoesNotContain("cursor=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReviewsAsync_PublishedFirstPage_SendsPublishedStatus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PageJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        await api.GetReviewsAsync(AdminReviewStatusFilter.Published, cursor: null, pageSize: 20);

        Assert.Equal("?status=published&pageSize=20", handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task GetReviewsAsync_CursorWithReservedCharacters_IsUrlEscaped()
    {
        const string cursor = "abc+/=def";

        var handler = new StubHandler(HttpStatusCode.OK, PageJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        await api.GetReviewsAsync(AdminReviewStatusFilter.Pending, cursor, pageSize: 20);

        var cursorParameter = handler.LastRequestUri!.Query
            .Split('&')
            .Single(part => part.StartsWith("cursor=", StringComparison.Ordinal));
        var escaped = cursorParameter["cursor=".Length..];

        // Курсор может содержать «+», «/» и «=»: без экранирования сервер получит другой курсор.
        Assert.Equal("abc%2B%2F%3Ddef", escaped);
        Assert.Equal(cursor, WebUtility.UrlDecode(escaped));
    }

    [Fact]
    public async Task GetReviewsAsync_Page_ParsesReviewWithClientMasterAndVisit()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PageJson("\"next-page\""));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        var page = await api.GetReviewsAsync(AdminReviewStatusFilter.Pending, cursor: null, pageSize: 20);

        var item = Assert.Single(page.Items);

        Assert.Equal(ReviewId, item.Id);
        Assert.Equal(4, item.Rating);
        Assert.Equal("Отличная стрижка", item.Text);
        Assert.False(item.IsPublished);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), item.CreatedAt);
        Assert.Equal(BarberId, item.Barber.Id);
        Assert.Equal("Иван Мастер", item.Barber.DisplayName);
        Assert.Equal(4.8m, item.Barber.Rating);
        Assert.Equal(12, item.Barber.ReviewsCount);
        Assert.True(item.Barber.IsActive);
        Assert.False(item.Barber.IsVerified);
        Assert.Equal(CustomerId, item.Customer.Id);
        Assert.Equal("Дмитрий Крылов", item.Customer.FullName);
        Assert.Equal("dmitry@example.com", item.Customer.Email);
        Assert.Equal(BookingId, item.Visit.BookingId);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 22, 30, 0, TimeSpan.Zero), item.Visit.StartAt);
        Assert.Equal(2400m, item.Visit.Price);
        Assert.Equal("Стрижка", item.Visit.ServiceName);
        Assert.Equal("Салон «Тест»", item.Visit.SalonName);
        Assert.Equal("Europe/Moscow", item.Visit.TimeZoneId);

        // Ненулевой курсор — единственный признак, что очередь можно догрузить.
        Assert.Equal("next-page", page.NextCursor);
    }

    [Fact]
    public async Task GetReviewsAsync_NotFound_ThrowsWithStatus()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "errorCodes": ["admin.reviews.status"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetReviewsAsync(AdminReviewStatusFilter.Pending, cursor: null, pageSize: 20));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("admin.reviews.status", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetReviewAsync_ById_SendsGetAndParsesReview()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ReviewJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        var review = await api.GetReviewAsync(ReviewId);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal($"/api/admin/reviews/{ReviewId:D}", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(ReviewId, review.Id);
        Assert.Equal("Иван Мастер", review.Barber.DisplayName);
        Assert.Equal("Дмитрий Крылов", review.Customer.FullName);
        Assert.Equal("Europe/Moscow", review.Visit.TimeZoneId);
    }

    [Fact]
    public async Task GetReviewAsync_NotFound_ThrowsWithStatusAndCode()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "errorCodes": ["review.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetReviewAsync(ReviewId));

        // Карточка уже разобрана: экран покажет «отзыв уже разобрали», а не «повторить».
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("review.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task PublishReviewAsync_Review_SendsPostToPublicationWithoutBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        await api.PublishReviewAsync(ReviewId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal($"/api/reviews/{ReviewId:D}/publication", handler.LastRequestUri!.AbsolutePath);

        // 204 без тела — успех: читать нечего, публикация выражается статусом.
        Assert.Null(handler.LastBody);
    }

    [Fact]
    public async Task HideReviewAsync_Review_SendsDeleteToPublicationWithoutBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        await api.HideReviewAsync(ReviewId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal($"/api/reviews/{ReviewId:D}/publication", handler.LastRequestUri!.AbsolutePath);
        Assert.Null(handler.LastBody);
    }

    [Fact]
    public async Task PublishReviewAsync_Forbidden_ThrowsWithStatusAndCode()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "Administrator role required.",
              "errorCodes": ["auth.forbidden"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Forbidden, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.PublishReviewAsync(ReviewId));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("auth.forbidden", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task HideReviewAsync_NotFound_ThrowsWithStatus()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "errorCodes": ["review.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminReviewsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.HideReviewAsync(ReviewId));

        // Отзыв уже разобрали — для карточки это тот же результат, что успех.
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    /// <summary>
    /// Ответ очереди: один отзыв с контекстом клиента, мастера и визита.
    /// </summary>
    /// <param name="nextCursor">JSON-значение курсора следующей страницы: строка либо <c>null</c>.</param>
    private static string PageJson(string nextCursor) => $$"""
        {
          "items": [{{ReviewJson()}}],
          "nextCursor": {{nextCursor}}
        }
        """;

    /// <summary>
    /// Один отзыв в том виде, в каком его отдаёт админский контроллер.
    /// </summary>
    private static string ReviewJson() => $$"""
        {
          "id": "{{ReviewId:D}}",
          "rating": 4,
          "text": "Отличная стрижка",
          "isPublished": false,
          "createdAt": "2026-11-15T10:00:00+00:00",
          "updatedAt": "2026-11-15T10:00:00+00:00",
          "barber": {
            "id": "{{BarberId:D}}",
            "displayName": "Иван Мастер",
            "rating": 4.8,
            "reviewsCount": 12,
            "isActive": true,
            "isVerified": false
          },
          "customer": {
            "id": "{{CustomerId:D}}",
            "fullName": "Дмитрий Крылов",
            "email": "dmitry@example.com"
          },
          "visit": {
            "bookingId": "{{BookingId:D}}",
            "startAt": "2026-11-14T22:30:00+00:00",
            "price": 2400,
            "serviceName": "Стрижка",
            "salonName": "Салон «Тест»",
            "timeZoneId": "Europe/Moscow"
          }
        }
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(HttpStatusCode statusCode, string? body = null, string contentType = "application/json")
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
