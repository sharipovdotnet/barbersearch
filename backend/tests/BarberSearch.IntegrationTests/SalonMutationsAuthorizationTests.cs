using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.UpdateSalonContactInfo;
using BarberSearch.Contracts.Salons.UpdateSalonLocation;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Каталог салонов открыт анонимно только на чтение: пять мутаций остаются админскими.
/// </summary>
/// <remarks>
/// Классовый <c>[Authorize(AdminOnly)]</c> снят ради <c>GET api/salons</c>, поэтому каждая
/// мутация проверяется отдельно: аноним — <c>401</c>, <c>Customer</c> и <c>Barber</c> —
/// <c>403</c>, администратор — прежние статусы.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class SalonMutationsAuthorizationTests
{
    private readonly PostgresFixture _database;

    public SalonMutationsAuthorizationTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Salon_mutations_are_admin_only_after_the_directory_opened()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var salon = await data.CreateSalonAsync("Authorization Salon");
        var anonymous = app.Client;

        await AssertAllAsync(
            () => SendMutationsAsync(anonymous, salon.Id),
            HttpStatusCode.Unauthorized);

        var customer = await app.CreateUserAsync(
            $"salon-mutations-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        await AssertAllAsync(
            () => SendMutationsAsync(app.Client, salon.Id),
            HttpStatusCode.Forbidden);

        var barber = await app.CreateUserAsync(
            $"salon-mutations-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        await AssertAllAsync(
            () => SendMutationsAsync(app.Client, salon.Id),
            HttpStatusCode.Forbidden);

        // Администратор: создание и правки проходят как раньше.
        var admin = await app.CreateUserAsync(
            $"salon-mutations-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var created = await app.Client.PostAsJsonAsync(
                   "api/salons",
                   NewSalonRequest("Admin Created Salon")))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using (var location = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salon.Id}/location",
                   new UpdateSalonLocationRequest(
                       "Neue Strasse 1",
                       "Potsdam",
                       "Germany",
                       "Europe/Berlin",
                       52.3906,
                       13.0645,
                       null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, location.StatusCode);
        }

        using (var contacts = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salon.Id}/contact-info",
                   new UpdateSalonContactInfoRequest("+4930000000", null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, contacts.StatusCode);
        }

        using (var deactivated = await app.Client.DeleteAsync(
                   $"api/salons/{salon.Id}/activation"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        }

        using (var activated = await app.Client.PostAsync(
                   $"api/salons/{salon.Id}/activation",
                   null))
        {
            Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        }
    }

    private static CreateSalonRequest NewSalonRequest(string name)
        => new(
            name,
            "Teststrasse 1",
            "Berlin",
            "Germany",
            "Europe/Berlin",
            52.5200,
            13.4050,
            null,
            null,
            null);

    private static async Task AssertAllAsync(
        Func<Task<List<HttpResponseMessage>>> send,
        HttpStatusCode expected)
    {
        var responses = await send();

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(expected, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// Пять мутаций салонов. Тело создания может быть валидным: отказ обязан прийти
    /// от авторизации, до домена.
    /// </summary>
    private static async Task<List<HttpResponseMessage>> SendMutationsAsync(
        HttpClient client,
        Guid salonId)
        => [
            await client.PostAsJsonAsync("api/salons", NewSalonRequest("Forbidden Salon")),
            await client.PutAsJsonAsync(
                $"api/salons/{salonId}/location",
                new UpdateSalonLocationRequest(
                    "Teststrasse 1",
                    "Berlin",
                    "Germany",
                    "Europe/Berlin",
                    52.5200,
                    13.4050,
                    null)),
            await client.PutAsJsonAsync(
                $"api/salons/{salonId}/contact-info",
                new UpdateSalonContactInfoRequest("+4930123456", null)),
            await client.PostAsync($"api/salons/{salonId}/activation", null),
            await client.DeleteAsync($"api/salons/{salonId}/activation")
        ];
}
