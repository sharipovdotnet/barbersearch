using System.Net;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// <c>SetPrimarySalon</c> (R-06) на реальной базе: переключение основного салона одним
/// <c>SaveChanges</c> проходит отложенное exclusion-ограничение <c>barber_salons</c>.
/// </summary>
/// <remarks>
/// До R-06 здесь был фильтрованный unique-индекс, и EF Core мог отправить
/// <c>is_primary = true</c> раньше <c>is_primary = false</c>: транзиентное состояние с двумя
/// основными салонами давало <c>409</c>. Тест ценен тем, что проверяет именно порядок
/// UPDATE-ов внутри одной транзакции и итоговое состояние в PostgreSQL, а не ответ домена.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PrimarySalonTests
{
    private readonly PostgresFixture _database;

    public PrimarySalonTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Switching_the_primary_salon_keeps_exactly_one_primary()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var email = $"primary-{Guid.NewGuid():N}@barbersearch.test";
        var seeded = await data.CreateBarberWithSalonsAsync("Primary Salon Barber", email);

        await app.AuthenticateAsync(email);

        using var switchToSecondary = await app.Client.PutAsync(
            $"api/barber-profiles/{seeded.Profile.Id}/salons/{seeded.Secondary.Id}/primary",
            null);
        Assert.Equal(HttpStatusCode.NoContent, switchToSecondary.StatusCode);

        var links = await data.GetSalonLinksAsync(seeded.Profile.Id);
        Assert.Equal(2, links.Count);
        Assert.Single(links, link => link.IsPrimary);
        Assert.Equal(seeded.Secondary.Id, links.Single(link => link.IsPrimary).SalonId);

        // Обратное переключение проверяет, что constraint не «залип» после первой смены.
        using var switchBack = await app.Client.PutAsync(
            $"api/barber-profiles/{seeded.Profile.Id}/salons/{seeded.Primary.Id}/primary",
            null);
        Assert.Equal(HttpStatusCode.NoContent, switchBack.StatusCode);

        links = await data.GetSalonLinksAsync(seeded.Profile.Id);
        Assert.Equal(2, links.Count);
        Assert.Single(links, link => link.IsPrimary);
        Assert.Equal(seeded.Primary.Id, links.Single(link => link.IsPrimary).SalonId);
    }
}
