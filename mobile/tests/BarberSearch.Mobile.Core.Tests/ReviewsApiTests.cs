using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Reviews.CreateReview;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента отзывов: тело запроса, разбор <c>201</c> и коды ошибок.
/// </summary>
/// <remarks>
/// Отзыв создаётся скрытым и уходит на модерацию, поэтому клиенту нужен только идентификатор
/// из ответа. Сеть подменяется <see cref="HttpMessageHandler"/>: тест не поднимает бэкенд,
/// но проходит через настоящий <see cref="HttpClient"/> и настоящие настройки JSON.
/// </remarks>
public sealed class ReviewsApiTests
{
    private static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000c1");
    private static readonly Guid ReviewId = Guid.Parse("9e100000-0000-4000-8000-0000000000c2");

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

    private static string CreatedJson() => $$"""{"id":"{{ReviewId:D}}"}""";

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
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
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
