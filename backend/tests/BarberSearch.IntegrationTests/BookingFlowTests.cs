using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Сквозной сценарий брони на реальной PostgreSQL: регистрация → вход по JWT → создание,
/// конфликт слота из exclusion-ограничения, отмена и повторное бронирование освободившегося слота.
/// </summary>
/// <remarks>
/// Unit-тесты с фейками не видят ни EF-маппинг, ни <c>ex_bookings_no_overlap</c>: здесь бронь
/// действительно вставляется в PostgreSQL и вторая вставка на тот же интервал отклоняется базой
/// (<c>23P01</c> → <c>409 booking.add.overlap</c>). JWT-ветка выбрана намеренно: cookie-вход
/// требует antiforgery и проверяется отдельно сценарием refresh.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class BookingFlowTests
{
    private readonly PostgresFixture _database;

    public BookingFlowTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_login_book_conflict_cancel_then_rebook_the_same_slot()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Booking Flow Barber");

        var email = $"booking-{Guid.NewGuid():N}@barbersearch.test";
        var customerId = await app.RegisterAsync(email);
        Assert.NotEqual(Guid.Empty, customerId);

        await app.AuthenticateAsync(email);

        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(30).AddHours(10), TimeSpan.Zero);
        var end = start.AddMinutes(30);

        using var first = await CreateBookingAsync(app.Client, barber, start, end);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var created = await first.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);

        using var conflict = await CreateBookingAsync(app.Client, barber, start, end);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var conflictBody = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("booking.add.overlap", conflictBody, StringComparison.Ordinal);
        Assert.Contains("errorCodes", conflictBody, StringComparison.Ordinal);

        using var cancel = await app.Client.PostAsync($"api/bookings/{created.Id}/cancellation", null);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        // Отменённая бронь больше не держит слот: частичный предикат exclusion-ограничения
        // пропускает только Pending/Confirmed.
        using var rebooked = await CreateBookingAsync(app.Client, barber, start, end);
        Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);
    }

    private static Task<HttpResponseMessage> CreateBookingAsync(
        HttpClient client,
        SeededBarber barber,
        DateTimeOffset start,
        DateTimeOffset end)
        => client.PostAsJsonAsync(
            "api/bookings",
            new CreateBookingRequest(
                barber.Profile.Id,
                barber.Service.Id,
                barber.Salon.Id,
                start,
                end,
                $"integration-{Guid.NewGuid():N}",
                "integration test"));
}
