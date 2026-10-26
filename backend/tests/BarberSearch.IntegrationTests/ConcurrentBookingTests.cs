using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Bookings.CreateBooking;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Конкурентные брони одного слота: N параллельных POST → ровно один <c>201</c>, остальные
/// <c>409</c>. Победителя определяет exclusion-ограничение PostgreSQL, а не проверка в коде.
/// </summary>
/// <remarks>
/// Сценарий проверяет и транзакционную advisory-блокировку барбера: без неё десятки
/// параллельных GiST-проверок могли бы дать <c>40P01</c> (deadlock), который маппится в
/// отдельный <c>409 booking.add.deadlock</c>, а не в <c>500</c>. Допустимы оба конфликтных
/// кода — важно, что вторых успешных вставок нет.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class ConcurrentBookingTests
{
    private const int ATTEMPTS = 6;

    private readonly PostgresFixture _database;

    public ConcurrentBookingTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 240_000)]
    public async Task Parallel_bookings_of_one_slot_produce_exactly_one_success()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Concurrent Barber");

        var email = $"concurrent-{Guid.NewGuid():N}@barbersearch.test";
        await app.RegisterAsync(email);
        await app.AuthenticateAsync(email);

        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(45).AddHours(12), TimeSpan.Zero);
        var end = start.AddMinutes(30);

        var requests = Enumerable.Range(0, ATTEMPTS)
            .Select(index => new CreateBookingRequest(
                barber.Profile.Id,
                barber.Service.Id,
                barber.Salon.Id,
                start,
                end,
                $"concurrent-{index}-{Guid.NewGuid():N}",
                "concurrent test"))
            .ToArray();

        var responses = await Task.WhenAll(requests.Select(request =>
            app.Client.PostAsJsonAsync("api/bookings", request)));

        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(ATTEMPTS - 1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

            foreach (var conflict in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
            {
                var body = await conflict.Content.ReadAsStringAsync();
                Assert.Contains("booking.add.", body, StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
