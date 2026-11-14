using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Reviews.CreateReview;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента отзывов: создание, «мои отзывы», правка, удаление и коды ошибок.
/// </summary>
/// <remarks>
/// <para>
/// Отзыв создаётся скрытым и уходит на модерацию, поэтому клиенту нужен только идентификатор
/// из ответа. Сеть подменяется <see cref="HttpMessageHandler"/>: тест не поднимает бэкенд,
/// но проходит через настоящий <see cref="HttpClient"/> и настоящие настройки JSON.
/// </para>
/// <para>
/// Курсор пагинации непрозрачен: клиент не разбирает его, но обязан передать обратно
/// без потерь, поэтому адрес проверяется и как экранированная строка, и после декодирования.
/// Ответ <c>204</c> собирается без содержимого: клиент не должен читать у него JSON.
/// </para>
/// </remarks>
public sealed class ReviewsApiTests
{
    private static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000c1");
    private static readonly Guid ReviewId = Guid.Parse("9e100000-0000-4000-8000-0000000000c2");
    private static readonly Guid BarberId = Guid.Parse("9e100000-0000-4000-8000-0000000000c3");

    /// <summary>
    /// Начало визита из ответа: 15 ноября 2026, 01:30 по поясу салона.
    /// </summary>
    private static readonly DateTimeOffset VisitStart = new(2026, 11, 14, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateReviewAsync_WithText_SendsBookingIdRatingAndText()
    {
        var handler = new StubHandler(HttpStatusCode.Created, CreatedJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        var id = await api.CreateReviewAsync(BookingId, 5, "Отличная стрижка");

        Assert.Equal(ReviewId, id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/reviews", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        // Автор и барбер в запросе не передаются: их выводит сервер из токена и брони.
        Assert.Equal(BookingId.ToString("D"), root.GetProperty("bookingId").GetString());
        Assert.Equal(5, root.GetProperty("rating").GetInt32());
        Assert.Equal("Отличная стрижка", root.GetProperty("text").GetString());

        var parsed = JsonSerializer.Deserialize<CreateReviewRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(BookingId, parsed.BookingId);
        Assert.Equal(5, parsed.Rating);
        Assert.Equal("Отличная стрижка", parsed.Text);
    }

    [Fact]
    public async Task CreateReviewAsync_EmptyText_SendsNull()
    {
        var handler = new StubHandler(HttpStatusCode.Created, CreatedJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        await api.CreateReviewAsync(BookingId, 4, null);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        // Пустой комментарий уходит как null, а не пустой строкой: сервер различает их
        // и не должен получить «отзыв с пустым текстом» вместо «отзыва без текста».
        Assert.Equal(JsonValueKind.Null, root.GetProperty("text").ValueKind);

        var parsed = JsonSerializer.Deserialize<CreateReviewRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Null(parsed.Text);
    }

    [Fact]
    public async Task CreateReviewAsync_Created_ParsesId()
    {
        var handler = new StubHandler(HttpStatusCode.Created, CreatedJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        var id = await api.CreateReviewAsync(BookingId, 3, null);

        // Идентификатор генерирует домен: клиент показывает успех только по непустому Guid.
        Assert.Equal(ReviewId, id);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CreateReviewAsync_Conflict_ThrowsWithCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "A review for this booking already exists.",
              "errorCodes": ["review.add.uniqueViolation"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateReviewAsync(BookingId, 5, "Повтор"));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);

        // Один отзыв на бронь: по этому коду экран говорит «второй оставить нельзя»,
        // английский detail контрактом не является.
        Assert.Contains("review.add.uniqueViolation", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateReviewAsync_Forbidden_ThrowsWithStatus()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "Only the customer of the booking can leave a review.",
              "errorCodes": ["auth.forbidden"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Forbidden, problemJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateReviewAsync(BookingId, 5, null));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task CreateReviewAsync_EmptyCreatedBody_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        // 201 без id — нарушение контракта: показывать успех при пустом Guid нельзя,
        // иначе экран закроется, а отзыва в списке не будет.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.CreateReviewAsync(BookingId, 5, "Отличная стрижка"));
    }

    [Fact]
    public async Task GetMyReviewsAsync_FirstPage_SendsPageSizeWithoutCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MyReviewsJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        await api.GetMyReviewsAsync(null, 10);

        var uri = handler.LastRequestUri!;

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/reviews/mine", uri.AbsolutePath);

        // Отсутствие курсора — это первая страница; пустая строка была бы невалидным курсором.
        Assert.Equal("?pageSize=10", uri.Query);
        Assert.DoesNotContain("cursor=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMyReviewsAsync_CursorWithReservedCharacters_IsUrlEscaped()
    {
        const string cursor = "abc+/=def";

        var handler = new StubHandler(HttpStatusCode.OK, MyReviewsJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        await api.GetMyReviewsAsync(cursor, 10);

        var query = handler.LastRequestUri!.Query;

        Assert.Contains("pageSize=10", query, StringComparison.Ordinal);

        var cursorParameter = query
            .Split('&')
            .Single(part => part.StartsWith("cursor=", StringComparison.Ordinal));
        var escaped = cursorParameter["cursor=".Length..];

        // Курсор непрозрачен и может содержать символы, недопустимые в адресе: «+», «/» и «=»
        // обязаны уехать экранированными, иначе сервер получит другой курсор.
        Assert.Equal("abc%2B%2F%3Ddef", escaped);
        Assert.Equal(cursor, WebUtility.UrlDecode(escaped));
    }

    [Fact]
    public async Task GetMyReviewsAsync_SingleReview_ParsesVisitAndStatus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MyReviewsJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        var page = await api.GetMyReviewsAsync(null, 10);

        var item = Assert.Single(page.Items);

        // Скрытый отзыв обязан приехать со своим контекстом визита: именно из него строится
        // строка «когда, услуга, салон, цена», а без неё список не подписать.
        Assert.Equal(ReviewId, item.Id);
        Assert.Equal(4, item.Rating);
        Assert.Null(item.Text);
        Assert.False(item.IsPublished);
        Assert.Equal(BarberId, item.Barber.Id);
        Assert.Equal("Иван Барбер", item.Barber.DisplayName);
        Assert.Equal(BookingId, item.Visit.BookingId);
        Assert.Equal(VisitStart, item.Visit.StartAt);
        Assert.Equal("Europe/Moscow", item.Visit.TimeZoneId);
        Assert.Equal(2400m, item.Visit.Price);
        Assert.Equal("Стрижка", item.Visit.ServiceName);
        Assert.Equal("Салон «Тест»", item.Visit.SalonName);

        // null — последняя страница: клиент не должен просить следующую по пустому курсору.
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetMyReviewsAsync_MorePages_ParsesNextCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MyReviewsJson("\"next-page\""));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        var page = await api.GetMyReviewsAsync(null, 10);

        // Ненулевой курсор — единственный признак, что список можно догрузить.
        Assert.Equal("next-page", page.NextCursor);
    }

    [Fact]
    public async Task UpdateReviewAsync_ChangedReview_SendsPutAndParsesStatus()
    {
        const string updatedJson = """
            {
              "isPublished": false,
              "updatedAt": "2026-11-15T10:15:00+00:00"
            }
            """;

        var handler = new StubHandler(HttpStatusCode.OK, updatedJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        var updated = await api.UpdateReviewAsync(ReviewId, 4, null);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal($"/api/reviews/{ReviewId:D}", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal(4, root.GetProperty("rating").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("text").ValueKind);

        var parsed = JsonSerializer.Deserialize<UpdateReviewRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(4, parsed.Rating);
        Assert.Null(parsed.Text);

        // Сервер сам решает, вернулся ли отзыв на модерацию: клиент показывает его ответ,
        // иначе строка «Опубликован» врала бы до перезагрузки кабинета.
        Assert.False(updated.IsPublished);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 15, 0, TimeSpan.Zero), updated.UpdatedAt);
    }

    [Fact]
    public async Task DeleteReviewAsync_ExistingReview_SendsDelete()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new ReviewsApi(httpClient);

        // 204 без тела — успех: читать нечего, отзыв уже удалён.
        await api.DeleteReviewAsync(ReviewId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal($"/api/reviews/{ReviewId:D}", handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task DeleteReviewAsync_NotFound_ThrowsWithStatusAndCode()
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
        var api = new ReviewsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.DeleteReviewAsync(ReviewId));

        // Чужой или уже удалённый отзыв: экран должен убрать строку, а не показать «удалено».
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("review.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    private static string CreatedJson() => $$"""{"id":"{{ReviewId:D}}"}""";

    /// <summary>
    /// Ответ «мои отзывы»: один скрытый отзыв с контекстом визита.
    /// </summary>
    /// <param name="nextCursor">JSON-значение курсора следующей страницы: строка либо <c>null</c>.</param>
    private static string MyReviewsJson(string nextCursor) => $$"""
        {
          "items": [
            {
              "id": "{{ReviewId:D}}",
              "rating": 4,
              "text": null,
              "isPublished": false,
              "createdAt": "2026-11-15T10:00:00+00:00",
              "updatedAt": "2026-11-15T10:00:00+00:00",
              "barber": {
                "id": "{{BarberId:D}}",
                "displayName": "Иван Барбер"
              },
              "visit": {
                "bookingId": "{{BookingId:D}}",
                "startAt": "2026-11-14T22:30:00+00:00",
                "timeZoneId": "Europe/Moscow",
                "price": 2400,
                "serviceName": "Стрижка",
                "salonName": "Салон «Тест»"
              }
            }
          ],
          "nextCursor": {{nextCursor}}
        }
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    /// <remarks>
    /// Тело ответа необязательно: у <c>204</c> содержимого нет, и клиент не должен его читать.
    /// </remarks>
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
