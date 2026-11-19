using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента админской сводки: адрес, разбор ответа и коды ошибок.
/// </summary>
/// <remarks>
/// <para>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест проходит через настоящий
/// <see cref="HttpClient"/> и настоящие настройки JSON (<see cref="ApiJson.Options"/>),
/// не поднимая бэкенд. Сводка — единственное агрегатное чтение админки, поэтому разбор
/// проверяется целиком: каждая очередь получает своё число.
/// </para>
/// <para>
/// Тело ответа может быть пустым: клиент обязан падать, а не показывать нули вместо сводки.
/// </para>
/// </remarks>
public sealed class AdminSummaryApiTests
{
    private static readonly Guid ReviewId = Guid.Parse("9e100000-0000-4000-8000-0000000000c2");

    [Fact]
    public async Task GetSummaryAsync_Request_SendsGetToAdminSummary()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SummaryJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminSummaryApi(httpClient);

        await api.GetSummaryAsync();

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/admin/summary", handler.LastRequestUri!.AbsolutePath);

        // Никаких параметров: роль сервер берёт из токена, а не из строки запроса.
        Assert.Equal(string.Empty, handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task GetSummaryAsync_SummaryBody_DeserialisesEveryQueue()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SummaryJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminSummaryApi(httpClient);

        var summary = await api.GetSummaryAsync();

        Assert.Equal(12, summary.Reviews.PendingCount);

        var oldest = Assert.IsType<AdminSummaryOldestReviewResponse>(summary.Reviews.Oldest);

        Assert.Equal(ReviewId, oldest.Id);
        Assert.Equal(4, oldest.Rating);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 9, 30, 0, TimeSpan.Zero), oldest.AwaitingSince);
        Assert.Equal("Игорь Мастер", oldest.BarberDisplayName);
        Assert.Equal("Ева Клиентова", oldest.CustomerDisplayName);

        Assert.Equal(3, summary.Barbers.UnverifiedCount);
        Assert.Equal(2, summary.Salons.InactiveCount);
        Assert.Equal(1, summary.Salons.PendingModerationCount);
        Assert.Equal(4, summary.Photos.PendingAvatarsCount);
        Assert.Equal(5, summary.Photos.PendingPortfolioCount);
        Assert.Equal(6, summary.Users.UnverifiedCount);
        Assert.Equal(7, summary.BarberRequests.PendingCount);
        Assert.Equal(8, summary.Bookings.ConfirmedCount);
    }

    [Fact]
    public async Task GetSummaryAsync_Forbidden_ThrowsWithStatusCode()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "The administrator role is required.",
              "errorCodes": ["auth.forbidden"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Forbidden, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminSummaryApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetSummaryAsync());

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("auth.forbidden", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyBody_ThrowsInsteadOfReturningNothing()
    {
        // Пустое тело — не JSON-значение null: чтение падает на разборе ещё до проверки
        // на null, поэтому InvalidOperationException из клиента здесь недостижима.
        var handler = new StubHandler(HttpStatusCode.OK, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminSummaryApi(httpClient);

        await Assert.ThrowsAsync<JsonException>(() => api.GetSummaryAsync());
    }

    [Fact]
    public async Task GetSummaryAsync_NullJsonBody_ThrowsInvalidOperationException()
    {
        // Литерал null разбирается в null, и только здесь срабатывает защита контракта:
        // показывать «все очереди пусты» вместо ответа сервера нельзя.
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminSummaryApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetSummaryAsync());
    }

    /// <summary>
    /// Ответ сводки: все семь очередей с разными числами, чтобы перепутанные поля были видны.
    /// </summary>
    /// <returns>JSON ответа <c>GET api/admin/summary</c>.</returns>
    private static string SummaryJson() => $$"""
        {
          "reviews": {
            "pendingCount": 12,
            "oldest": {
              "id": "{{ReviewId:D}}",
              "rating": 4,
              "awaitingSince": "2026-11-17T09:30:00+00:00",
              "barberDisplayName": "Игорь Мастер",
              "customerDisplayName": "Ева Клиентова"
            }
          },
          "barbers": {
            "unverifiedCount": 3
          },
          "salons": {
            "inactiveCount": 2,
            "pendingModerationCount": 1
          },
          "photos": {
            "pendingAvatarsCount": 4,
            "pendingPortfolioCount": 5
          },
          "users": {
            "unverifiedCount": 6
          },
          "barberRequests": {
            "pendingCount": 7
          },
          "bookings": {
            "confirmedCount": 8
          }
        }
        """;

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
