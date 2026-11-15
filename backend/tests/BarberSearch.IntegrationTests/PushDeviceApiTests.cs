using System.Net;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// API устройств: аноним получает <c>401</c>, регистрация идемпотентна, токен переходит
/// к новому аккаунту, снятие неизвестного/чужого токена — успех без эффекта.
/// </summary>
/// <remarks>
/// Мобильный клиент ходит с Bearer, поэтому CSRF-заголовок здесь не нужен: фильтр мутаций
/// пропускает bearer-запросы. Владелец устройства всегда берётся из сессии.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PushDeviceApiTests
{
    private const string Endpoint = "api/push/devices";
    private const string UnregisterEndpoint = "api/push/devices/unregister";

    private readonly PostgresFixture _database;

    public PushDeviceApiTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_request_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        using var client = app.CreateClient();

        using var response = await TestApp.PostJsonAsync(client, Endpoint, new
        {
            token = "anonymous-device",
            platform = "Android"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_is_idempotent_and_unregister_removes_the_token()
    {
        await using var app = await TestApp.StartAsync(_database);

        var email = $"push-device-{Guid.NewGuid():N}@barbersearch.test";
        var user = await app.CreateUserAsync(email);
        await app.AuthenticateAsync(email);

        var push = new PushTestData(app);

        using (var first = await TestApp.PostJsonAsync(app.Client, Endpoint, new
        {
            token = "device-token-1",
            platform = "Android"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using (var second = await TestApp.PostJsonAsync(app.Client, Endpoint, new
        {
            token = "device-token-1",
            platform = "Android"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        var device = Assert.Single(await push.GetDeviceTokensAsync(user.Id));
        Assert.Equal("device-token-1", device.Token);

        using (var unregister = await TestApp.PostJsonAsync(app.Client, UnregisterEndpoint, new
        {
            token = "device-token-1"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, unregister.StatusCode);
        }

        Assert.Empty(await push.GetDeviceTokensAsync(user.Id));

        using (var repeated = await TestApp.PostJsonAsync(app.Client, UnregisterEndpoint, new
        {
            token = "device-token-1"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_from_another_account_transfers_the_token_and_foreign_unregister_keeps_it()
    {
        await using var app = await TestApp.StartAsync(_database);

        var firstEmail = $"push-owner-{Guid.NewGuid():N}@barbersearch.test";
        var firstUser = await app.CreateUserAsync(firstEmail);
        await app.AuthenticateAsync(firstEmail);

        using (var registered = await TestApp.PostJsonAsync(app.Client, Endpoint, new
        {
            token = "shared-phone-token",
            platform = "Android"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        }

        var secondEmail = $"push-new-owner-{Guid.NewGuid():N}@barbersearch.test";
        var secondUser = await app.CreateUserAsync(secondEmail);
        await app.AuthenticateAsync(secondEmail);

        using (var transferred = await TestApp.PostJsonAsync(app.Client, Endpoint, new
        {
            token = "shared-phone-token",
            platform = "Android"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, transferred.StatusCode);
        }

        var push = new PushTestData(app);
        Assert.Empty(await push.GetDeviceTokensAsync(firstUser.Id));

        var device = Assert.Single(await push.GetDeviceTokensAsync(secondUser.Id));
        Assert.Equal(secondUser.Id, device.UserId);

        // Прежний владелец пробует снять уже чужой токен: успех без эффекта, строка остаётся.
        await app.AuthenticateAsync(firstEmail);

        using (var foreignUnregister = await TestApp.PostJsonAsync(app.Client, UnregisterEndpoint, new
        {
            token = "shared-phone-token"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, foreignUnregister.StatusCode);
        }

        Assert.Single(await push.GetDeviceTokensAsync(secondUser.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_with_an_older_clock_does_not_move_last_seen_backwards()
    {
        await using var app = await TestApp.StartAsync(_database);

        var email = $"push-clock-{Guid.NewGuid():N}@barbersearch.test";
        var user = await app.CreateUserAsync(email);
        await app.AuthenticateAsync(email);

        using (var registered = await TestApp.PostJsonAsync(app.Client, Endpoint, new
        {
            token = "clock-device",
            platform = "Android"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        }

        var future = DateTimeOffset.UtcNow.AddHours(1);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE push_device_tokens
                SET last_seen_at = {future}
                WHERE token = {"clock-device"} AND user_id = {user.Id}
                """);
        }

        using (var again = await TestApp.PostJsonAsync(app.Client, Endpoint, new
        {
            token = "clock-device",
            platform = "Android"
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        var push = new PushTestData(app);
        var device = Assert.Single(await push.GetDeviceTokensAsync(user.Id));

        // GREATEST(existing, EXCLUDED): более ранние часы не откатывают метку назад.
        Assert.True(
            device.LastSeenAt >= future.AddSeconds(-1),
            $"Expected at least {future:O}, actual {device.LastSeenAt:O}.");
    }

    [Fact(Timeout = 180_000)]
    public async Task Parallel_registrations_never_exceed_the_device_cap()
    {
        await using var app = await TestApp.StartAsync(_database);

        var email = $"push-cap-{Guid.NewGuid():N}@barbersearch.test";
        var user = await app.CreateUserAsync(email);
        await app.AuthenticateAsync(email);

        for (var index = 0; index < 5; index++)
        {
            using var seeded = await TestApp.PostJsonAsync(app.Client, Endpoint, new
            {
                token = $"cap-device-{index}",
                platform = "Android"
            });

            Assert.Equal(HttpStatusCode.NoContent, seeded.StatusCode);
        }

        // Одиннадцать параллельных регистраций одного пользователя: без advisory-блокировки
        // READ COMMITTED оставил бы больше пяти строк.
        var parallel = Enumerable.Range(0, 11)
            .Select(index => TestApp.PostJsonAsync(app.Client, Endpoint, new
            {
                token = $"cap-parallel-{index}",
                platform = "Android"
            }))
            .ToArray();

        var responses = await Task.WhenAll(parallel);

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var push = new PushTestData(app);
        Assert.Equal(5, await push.CountDeviceTokensAsync(user.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_with_blank_token_returns_the_domain_validation_code()
    {
        await using var app = await TestApp.StartAsync(_database);

        var email = $"push-validation-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);
        await app.AuthenticateAsync(email);

        using var response = await TestApp.PostJsonAsync(app.Client, Endpoint, new
        {
            token = "   ",
            platform = "Android"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("pushDeviceToken.token.invalid", body, StringComparison.Ordinal);
    }
}
