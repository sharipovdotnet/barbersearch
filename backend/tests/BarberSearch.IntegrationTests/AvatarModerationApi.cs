using System.Net;
using System.Net.Http.Json;
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
/// <para>
/// С премодерацией к «скрыть/вернуть» добавились решения «одобрить/отклонить». Помощники
/// возвращают сырой ответ, когда тесту нужен отказ (409/400), и проверяют <c>204</c>, когда
/// сценарий ожидает успех.
/// </para>
/// </remarks>
internal static class AvatarModerationApi
{
    public static async Task BlockAsync(AdminAvatarSession admin, Guid userId)
    {
        using var response = await SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/admin/users/{userId}/avatar/block");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task UnblockAsync(AdminAvatarSession admin, Guid userId)
    {
        using var response = await SendAdminAsync(
            admin,
            HttpMethod.Delete,
            $"api/admin/users/{userId}/avatar/block");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Одобряет аватар и проверяет <c>204</c>: сценарии, которым важен только результат.
    /// </summary>
    public static async Task ApproveAsync(AdminAvatarSession admin, Guid userId)
    {
        using var response = await SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/admin/users/{userId}/avatar/approval");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Отклоняет аватар с причиной и проверяет <c>204</c>.
    /// </summary>
    public static async Task RejectAsync(AdminAvatarSession admin, Guid userId, string? reason)
    {
        using var response = await SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/admin/users/{userId}/avatar/rejection",
            new { reason });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Отправляет админскую мутацию cookie-сессией с CSRF-заголовком и возвращает ответ
    /// как есть: вызывающий проверяет и <c>204</c>, и доменные отказы.
    /// </summary>
    public static Task<HttpResponseMessage> SendAdminAsync(
        AdminAvatarSession admin,
        HttpMethod method,
        string path,
        object? body = null)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-CSRF-TOKEN", admin.CsrfToken);

        return admin.Client.SendAsync(request);
    }

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
