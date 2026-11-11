using System.Net;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админские вызовы модерации аватара для интеграционных тестов.
/// </summary>
/// <remarks>
/// Мутации идут cookie-сессией с <c>X-CSRF-TOKEN</c>, как браузер: это проверяет и сам
/// маршрут, и глобальный antiforgery-фильтр. Один помощник на все спеки, чтобы матрица
/// «аноним / владелец / админ» не разъезжалась по файлам.
/// </remarks>
internal static class AvatarModerationApi
{
    public static Task BlockAsync(AdminAvatarSession admin, Guid userId)
        => SendAsync(admin, HttpMethod.Post, userId);

    public static Task UnblockAsync(AdminAvatarSession admin, Guid userId)
        => SendAsync(admin, HttpMethod.Delete, userId);

    /// <summary>
    /// Создаёт администратора и открывает его cookie-сессию.
    /// </summary>
    public static async Task<AdminAvatarSession> StartAdminSessionAsync(TestApp app)
    {
        var admin = await app.CreateUserAsync(
            $"avatar-moderation-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var session = await app.SignInWithCookieAsync(admin.Email);

        return new AdminAvatarSession(admin, session);
    }

    private static async Task SendAsync(
        AdminAvatarSession admin,
        HttpMethod method,
        Guid userId)
    {
        using var request = new HttpRequestMessage(
            method,
            $"api/admin/users/{userId}/avatar/block");

        request.Headers.Add("X-CSRF-TOKEN", admin.CsrfToken);

        using var response = await admin.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

/// <summary>
/// Администратор и его cookie-сессия: тесту нужны и автор строки аудита, и браузерный клиент.
/// </summary>
internal sealed class AdminAvatarSession : IAsyncDisposable
{
    public AdminAvatarSession(User admin, CookieSession session)
    {
        Admin = admin;
        Session = session;
    }

    public User Admin { get; }

    public CookieSession Session { get; }

    public HttpClient Client => Session.Client;

    public string CsrfToken => Session.CsrfToken;

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}
