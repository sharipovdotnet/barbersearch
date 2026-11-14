using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента создания брони: тело запроса, разбор ответа и ошибок.
/// </summary>
/// <remarks>
/// Время проверяется разбором отправленного JSON, а не сравнением строк: клиент обязан
/// отправить тот же инстант, что вернул <c>/slots</c>, и не пересчитывать его в пояс телефона.
/// </remarks>
public sealed class BookingsApiTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid BarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000004");
    private static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid CreatedId = Guid.Parse("9e100000-0000-4000-8000-00000000000a");

    private static readonly DateTimeOffset StartAt = new(2026, 11, 15, 18, 30, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset EndAt = new(2026, 11, 15, 19, 15, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task CreateBookingAsync_AllFields_SendsThemAndParsesCreatedId()
    {
        var handler = new StubHandler(HttpStatusCode.Created, CreatedJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);
        var request = new CreateBookingRequest(
            BarberProfileId,
            BarberServiceId,
            SalonId,
            StartAt,
            EndAt,
            "0123456789abcdef0123456789abcdef",
            CustomerNote: null);

        var id = await api.CreateBookingAsync(request);

        Assert.Equal(CreatedId, id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/bookings", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal(BarberProfileId.ToString("D"), root.GetProperty("barberProfileId").GetString());
        Assert.Equal(BarberServiceId.ToString("D"), root.GetProperty("barberServiceId").GetString());
        Assert.Equal(SalonId.ToString("D"), root.GetProperty("salonId").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("customerNote").ValueKind);

        // Пустая заметка уходит как null, а не пустой строкой.
        var parsed = JsonSerializer.Deserialize<CreateBookingRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(StartAt, parsed.StartAt);
        Assert.Equal(StartAt.UtcDateTime, parsed.StartAt.UtcDateTime);
        Assert.Equal(StartAt.Offset, parsed.StartAt.Offset);
        Assert.Equal(EndAt, parsed.EndAt);
        Assert.Null(parsed.CustomerNote);
    }

    [Fact]
    public async Task CreateBookingAsync_Note_PassedAsIs()
    {
        var handler = new StubHandler(HttpStatusCode.Created, CreatedJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        await api.CreateBookingAsync(new CreateBookingRequest(
            BarberProfileId,
            BarberServiceId,
            SalonId,
            StartAt,
            EndAt,
            "key",
            "Покороче сзади"));

        var parsed = JsonSerializer.Deserialize<CreateBookingRequest>(handler.LastBody!, ApiJson.Options);

        Assert.Equal("Покороче сзади", parsed!.CustomerNote);
    }

    [Fact]
    public async Task CreateBookingAsync_Conflict_TurnsProblemDetailsIntoException()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The slot is occupied.",
              "errorCodes": ["booking.add.overlap"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateBookingAsync(Request()));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains(ApiErrorCodes.Booking.AddOverlap, exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateBookingAsync_EmptyCreatedBody_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateBookingAsync(Request()));
    }

    private static CreateBookingRequest Request() => new(
        BarberProfileId,
        BarberServiceId,
        SalonId,
        StartAt,
        EndAt,
        "0123456789abcdef0123456789abcdef",
        CustomerNote: null);

    private static string CreatedJson() => $$"""{"id":"{{CreatedId:D}}"}""";

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
