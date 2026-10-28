using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админское чтение пользователей на реальной PostgreSQL: список с поиском и фильтром роли,
/// карточка с профилем барбера, закрытость от не-администраторов и защита последнего админа.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AdminUserReadTests
{
    private readonly PostgresFixture _database;

    /// <summary>
    /// API отдаёт перечисления строками (<c>UserRole</c>), а <see cref="HttpClient"/>
    /// по умолчанию ждёт числа: без конвертера чтение роли падает на первом ответе.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AdminUserReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_reads_user_list_and_card_with_barber_profile()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"admin-users-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        var barber = await new TestData(app).CreateBookableBarberAsync("Admin Users Barber");

        // Профиль барбера связывается с пользователем в домене: админская карточка показывает
        // его имя и два статуса, а публичный контракт профиля не меняется.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Id == barber.User.Id);

            var linked = user.LinkBarberProfile(barber.Profile.Id);
            Assert.False(linked.IsFailure);

            await db.SaveChangesAsync();
        }

        await app.AuthenticateAsync(admin.Email);

        using var list = await app.Client.GetAsync($"api/admin/users?search={barber.User.Email}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var page = await list.Content.ReadFromJsonAsync<PagedResponse<AdminUserResponse>>(JsonOptions);
        Assert.NotNull(page);

        var item = Assert.Single(page.Items);
        Assert.Equal(barber.User.Id, item.Id);
        Assert.Equal(barber.User.Email, item.Email);
        Assert.Equal(UserRole.Barber, item.Role);
        Assert.True(item.IsActive);
        Assert.Equal(barber.Profile.Id, item.BarberProfileId);

        // Фильтр роли: барбер не попадает в список администраторов.
        using var admins = await app.Client.GetAsync(
            $"api/admin/users?role=Admin&search={barber.User.Email}");
        Assert.Equal(HttpStatusCode.OK, admins.StatusCode);

        var adminsPage = await admins.Content
            .ReadFromJsonAsync<PagedResponse<AdminUserResponse>>(JsonOptions);
        Assert.NotNull(adminsPage);
        Assert.Empty(adminsPage.Items);

        using var detail = await app.Client.GetAsync($"api/admin/users/{barber.User.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<AdminUserDetailResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(barber.User.Id, body.Id);
        Assert.NotNull(body.BarberProfile);
        Assert.Equal(barber.Profile.Id, body.BarberProfile.Id);
        Assert.Equal("Admin Users Barber", body.BarberProfile.DisplayName);
        Assert.True(body.BarberProfile.IsActive);
    }

    [Fact(Timeout = 120_000)]
    public async Task Admin_users_are_closed_for_anonymous_and_customers()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var anonymous = await app.Client.GetAsync("api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var customer = await app.CreateUserAsync(
            $"admin-users-forbidden-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var forbidden = await app.Client.GetAsync("api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact(Timeout = 120_000)]
    public async Task Admin_cannot_change_own_role_or_block_self()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"admin-self-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);

        using var role = await app.Client.PutAsJsonAsync(
            $"api/users/{admin.Id}/role",
            new { role = nameof(UserRole.Customer) });
        Assert.Equal(HttpStatusCode.Conflict, role.StatusCode);

        var roleBody = await role.Content.ReadAsStringAsync();
        Assert.Contains("user.role.self", roleBody, StringComparison.Ordinal);

        using var block = await app.Client.DeleteAsync($"api/users/{admin.Id}/activation");
        Assert.Equal(HttpStatusCode.Conflict, block.StatusCode);

        var blockBody = await block.Content.ReadAsStringAsync();
        Assert.Contains("user.deactivation.self", blockBody, StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task Admin_can_demote_another_admin_while_two_exist()
    {
        await using var app = await TestApp.StartAsync(_database);
        var actor = await app.CreateUserAsync(
            $"admin-actor-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        var target = await app.CreateUserAsync(
            $"admin-target-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(actor.Email);

        using var demoted = await app.Client.PutAsJsonAsync(
            $"api/users/{target.Id}/role",
            new { role = nameof(UserRole.Customer) });
        Assert.Equal(HttpStatusCode.NoContent, demoted.StatusCode);

        using var detail = await app.Client.GetAsync($"api/admin/users/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<AdminUserDetailResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(UserRole.Customer, body.Role);
    }
}
