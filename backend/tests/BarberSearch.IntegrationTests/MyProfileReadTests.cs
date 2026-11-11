using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Чтение собственного профиля для кабинета: имя, телефон, роль и подтверждения приходят
/// из базы, а чужой профиль этим адресом не открыть.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MyProfileReadTests
{
    private readonly PostgresFixture _database;

    /// <summary>
    /// API отдаёт <c>UserRole</c> строкой, а <see cref="HttpClient"/> по умолчанию ждёт число.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public MyProfileReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Customer_reads_own_profile_with_actual_names_and_phone()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(
            $"my-profile-{Guid.NewGuid():N}@barbersearch.test");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tracked = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);

            // Имя в ответе должно прийти из базы, а не из cookie: профиль правится отдельно
            // от аутентификации, и кабинет обязан видеть свежие значения.
            var updated = tracked.UpdateProfile(
                "Мария",
                "Кузнецова",
                "+49 170 1234567",
                avatarUrl: null);
            Assert.False(updated.IsFailure);

            await db.SaveChangesAsync();
        }

        await app.AuthenticateAsync(user.Email);

        using var response = await app.Client.GetAsync("api/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MyProfileResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(user.Id, body.Id);
        Assert.Equal(user.Email, body.Email);
        Assert.Equal("Мария", body.FirstName);
        Assert.Equal("Кузнецова", body.LastName);
        Assert.Equal("+49 170 1234567", body.PhoneNumber);
        Assert.Null(body.AvatarUrl);
        Assert.False(body.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Approved, body.AvatarModerationStatus);
        Assert.Null(body.AvatarRejectionReason);
        Assert.Equal(UserRole.Customer, body.Role);
        Assert.Null(body.BarberProfileId);
        Assert.False(body.IsEmailVerified);
        Assert.False(body.IsPhoneVerified);
        Assert.NotEqual(default, body.CreatedAt);
    }

    [Fact(Timeout = 120_000)]
    public async Task Profile_is_closed_for_anonymous()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var response = await app.Client.GetAsync("api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
