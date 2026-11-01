using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.ChangePassword;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Refresh;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Глобальный antiforgery-фильтр для cookie-мутаций: без валидной пары
/// <c>cookie + X-CSRF-TOKEN</c> действие не выполняется, а Bearer и анонимные сценарии
/// чувствуют себя как до этапа #35.
/// </summary>
/// <remarks>
/// Каждый негативный кейс проверяет и отсутствие побочного эффекта (строки в БД), а не только
/// код ответа: фильтр обязан отклонять запрос **до** действия, иначе <c>400</c> ничего не
/// доказывает. Токен берётся заново после cookie-входа — он привязан к личности.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class CsrfMutationTests
{
    /// <summary>
    /// Заведомо невалидный заголовок: парной antiforgery-cookie у него нет.
    /// </summary>
    private const string BROKEN_CSRF_TOKEN = "broken-antiforgery-token";

    private readonly PostgresFixture _database;

    public CsrfMutationTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_booking_creation_is_unauthorized_not_a_csrf_failure()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Anonymous Csrf Barber");

        using var response = await app.Client.PostAsJsonAsync(
            "api/bookings",
            NewBooking(barber));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountBookingsAsync(app));
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_booking_without_csrf_token_is_rejected_and_persists_nothing()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Cookie Csrf Barber");
        var email = $"csrf-cookie-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email);

        using var response = await session.Client.PostAsJsonAsync(
            "api/bookings",
            NewBooking(barber));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountBookingsAsync(app));
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_booking_with_broken_csrf_token_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Broken Csrf Barber");
        var email = $"csrf-broken-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email);

        using var response = await SendWithCsrfAsync(
            session,
            HttpMethod.Post,
            "api/bookings",
            NewBooking(barber),
            BROKEN_CSRF_TOKEN);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountBookingsAsync(app));
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_booking_with_fresh_csrf_token_is_created()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Fresh Csrf Barber");
        var email = $"csrf-fresh-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email);

        using var response = await SendWithCsrfAsync(
            session,
            HttpMethod.Post,
            "api/bookings",
            NewBooking(barber),
            session.CsrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountBookingsAsync(app));
    }

    [Fact(Timeout = 180_000)]
    public async Task Bearer_booking_creation_still_does_not_require_csrf_token()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Bearer Csrf Barber");
        var email = $"csrf-bearer-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);
        await app.AuthenticateAsync(email);

        using var response = await app.Client.PostAsJsonAsync(
            "api/bookings",
            NewBooking(barber));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountBookingsAsync(app));
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_publication_without_csrf_token_does_not_publish_the_review()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Publish Csrf Barber");
        var author = await app.CreateUserAsync($"csrf-author-{Guid.NewGuid():N}@barbersearch.test");
        var reviewId = await data.CreateReviewAsync(barber, author.Id, published: false);
        var admin = await app.CreateUserAsync(
            $"csrf-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await using var session = await app.SignInWithCookieAsync(admin.Email);

        using var response = await session.Client.PostAsync(
            $"api/reviews/{reviewId}/publication",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());
        Assert.False(await IsReviewPublishedAsync(app, reviewId));
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_hiding_without_csrf_token_does_not_hide_the_review()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Hide Csrf Barber");
        var author = await app.CreateUserAsync($"csrf-hide-author-{Guid.NewGuid():N}@barbersearch.test");
        var reviewId = await data.CreateReviewAsync(barber, author.Id, published: true);
        var admin = await app.CreateUserAsync(
            $"csrf-hide-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await using var session = await app.SignInWithCookieAsync(admin.Email);

        using var response = await session.Client.DeleteAsync(
            $"api/reviews/{reviewId}/publication");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());
        Assert.True(await IsReviewPublishedAsync(app, reviewId));
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_user_activation_without_csrf_token_does_not_activate_the_user()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var target = await data.CreateBlockedUserAsync(
            $"csrf-blocked-{Guid.NewGuid():N}@barbersearch.test");
        var admin = await app.CreateUserAsync(
            $"csrf-activation-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await using var session = await app.SignInWithCookieAsync(admin.Email);

        using var response = await session.Client.PostAsync(
            $"api/users/{target.Id}/activation",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());
        Assert.False(await IsUserActiveAsync(app, target.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_read_without_csrf_token_stays_allowed()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"csrf-read-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email);

        using var response = await session.Client.GetAsync("api/bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_registration_stays_without_csrf_token()
    {
        await using var app = await TestApp.StartAsync(_database);

        var id = await app.RegisterAsync($"csrf-register-{Guid.NewGuid():N}@barbersearch.test");

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_cookie_login_without_csrf_token_is_still_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"csrf-login-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        using var response = await app.Client.PostAsJsonAsync(
            "https://localhost/api/auth/login/cookie",
            new LoginRequest(email, TestApp.Password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_refresh_without_csrf_token_is_still_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"csrf-refresh-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email);

        using var response = await session.Client.PostAsync("api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_refresh_with_token_in_body_stays_allowed()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"csrf-refresh-body-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);
        var tokens = await app.LoginAsync(email);

        using var response = await app.Client.PostAsJsonAsync(
            "api/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refreshed = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.False(
            response.Headers.Contains("Set-Cookie"),
            "Body-ветка refresh не ставит cookie.");
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_logout_without_csrf_token_is_rejected_and_keeps_the_session()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"csrf-logout-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email);

        using var logout = await session.Client.PostAsync("api/auth/logout", null);

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Contains("auth.csrf.invalid", await logout.Content.ReadAsStringAsync());

        using var me = await session.Client.GetAsync("api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_change_password_without_csrf_token_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"csrf-change-cookie-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email);

        using var response = await session.Client.PostAsJsonAsync(
            "api/auth/change-password",
            new ChangePasswordRequest(TestApp.Password, TestApp.Password + "New"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.csrf.invalid", await response.Content.ReadAsStringAsync());

        // Пароль не изменился: старый вход по-прежнему проходит.
        using var login = await app.Client.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest(email, TestApp.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Bearer_change_password_does_not_require_csrf_token()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"csrf-change-bearer-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);
        await app.AuthenticateAsync(email);

        using var response = await app.Client.PostAsJsonAsync(
            "api/auth/change-password",
            new ChangePasswordRequest(TestApp.Password, TestApp.Password + "New"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Действие действительно выполнилось: новый пароль проходит вход.
        using var login = await app.Client.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest(email, TestApp.Password + "New"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static CreateBookingRequest NewBooking(SeededBarber barber)
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(30).AddHours(10), TimeSpan.Zero);

        return new CreateBookingRequest(
            barber.Profile.Id,
            barber.Service.Id,
            barber.Salon.Id,
            start,
            start.AddMinutes(30),
            $"csrf-{Guid.NewGuid():N}",
            "csrf test");
    }

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(
        CookieSession session,
        HttpMethod method,
        string path,
        object? body,
        string? csrfToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrfToken is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        }

        return await session.Client.SendAsync(request);
    }

    private static async Task<int> CountBookingsAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Bookings.AsNoTracking().CountAsync();
    }

    private static async Task<bool> IsReviewPublishedAsync(TestApp app, Guid reviewId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Reviews
            .AsNoTracking()
            .Where(review => review.Id == reviewId)
            .Select(review => review.IsPublished)
            .SingleAsync();
    }

    private static async Task<bool> IsUserActiveAsync(TestApp app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.IsActive)
            .SingleAsync();
    }
}
