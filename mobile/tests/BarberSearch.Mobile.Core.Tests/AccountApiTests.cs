using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Auth.ChangePassword;
using BarberSearch.Contracts.Users.UpdateUserProfile;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента аккаунта: текущий пользователь, профиль, пароль и смена email.
/// </summary>
/// <remarks>
/// <para>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест не поднимает бэкенд, но проходит
/// через настоящий <see cref="HttpClient"/> и настоящие настройки JSON (<c>ApiJson.Options</c>),
/// поэтому ловит и ошибки адреса, и ошибки сериализации контрактов.
/// </para>
/// <para>
/// Тело запроса проверяется дважды: разбором JSON как документа (видно <c>null</c> вместо
/// пустых строк) и десериализацией в контракт бэкенда (видно, что клиент не придумал своё поле).
/// Ответ <c>204</c> собирается без содержимого: клиент не должен читать у него JSON.
/// </para>
/// </remarks>
public sealed class AccountApiTests
{
    private const string UserId = "0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f70";
    private const string AvatarUrl = "https://cdn.example.test/avatar.jpg";

    /// <summary>
    /// JSON-значение последнего входа: инстант из базы, а не строка «когда-нибудь».
    /// </summary>
    private const string LastLoginJson = "\"2026-11-01T12:30:00+00:00\"";

    private const string MeJson = """
        {
          "id": "0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f70",
          "email": "mobile-test@barbersearch.test",
          "role": "Customer",
          "barberProfileId": null,
          "isEmailVerified": true
        }
        """;

    [Fact]
    public async Task GetMeAsync_CurrentUser_ParsesResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MeJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var me = await api.GetMeAsync();

        Assert.Equal("mobile-test@barbersearch.test", me.Email);
        Assert.Equal("Customer", me.Role);
        Assert.Null(me.BarberProfileId);
        Assert.True(me.IsEmailVerified);
        Assert.Equal("/api/auth/me", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetMeAsync_Unauthorized_TurnsIntoException()
    {
        const string problemJson = """
            {
              "status": 401,
              "errorCodes": ["auth.unauthorized"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Unauthorized, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(() => api.GetMeAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("auth.unauthorized", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetMyProfileAsync_FullProfile_ParsesResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ProfileJson(LastLoginJson));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var profile = await api.GetMyProfileAsync();

        // Экран профиля читает базу, а не claims: имя, аватар и подтверждения должны приехать
        // из ответа целиком, иначе карточка покажет устаревшие данные из токена.
        Assert.Equal(Guid.Parse(UserId), profile.Id);
        Assert.Equal("mobile-test@barbersearch.test", profile.Email);
        Assert.Equal("Ева", profile.FirstName);
        Assert.Equal("Клиентова", profile.LastName);
        Assert.Null(profile.PhoneNumber);
        Assert.Equal(AvatarUrl, profile.AvatarUrl);
        Assert.Equal(UserRole.Customer, profile.Role);
        Assert.Null(profile.BarberProfileId);
        Assert.True(profile.IsEmailVerified);
        Assert.False(profile.IsPhoneVerified);
        Assert.Equal(new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero), profile.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 30, 0, TimeSpan.Zero), profile.LastLoginAt);
        Assert.False(profile.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Approved, profile.AvatarModerationStatus);
        Assert.Null(profile.AvatarRejectionReason);
        Assert.Equal("/api/users/me", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetMyProfileAsync_NoLastLogin_ParsesNull()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ProfileJson("null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var profile = await api.GetMyProfileAsync();

        // «Ещё не было» рисуется только по null: 1970-01-01 вместо него показал бы ложную дату.
        Assert.Null(profile.LastLoginAt);
        Assert.Equal("/api/users/me", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidProfile_SendsPutToUserProfilePath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        await api.UpdateProfileAsync(Guid.Parse(UserId), "Ева", "Клиентова", null, AvatarUrl);

        // Идентификатор в адресе — из профиля: клиент не сохраняет «свой» профиль догадкой.
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal($"/api/users/{UserId}/profile", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal("Ева", root.GetProperty("firstName").GetString());
        Assert.Equal("Клиентова", root.GetProperty("lastName").GetString());
        Assert.Equal(AvatarUrl, root.GetProperty("avatarUrl").GetString());

        // Пустой телефон уходит как null: пустая строка стёрла бы номер иначе, чем «не задан».
        Assert.Equal(JsonValueKind.Null, root.GetProperty("phoneNumber").ValueKind);

        var parsed = JsonSerializer.Deserialize<UpdateUserProfileRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Ева", parsed.FirstName);
        Assert.Equal("Клиентова", parsed.LastName);
        Assert.Null(parsed.PhoneNumber);
        Assert.Equal(AvatarUrl, parsed.AvatarUrl);
    }

    [Fact]
    public async Task UpdateProfileAsync_NullAvatarAndPhone_SucceedsWithoutReadingContent()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        // 204 без тела — нормальный успех: клиент не должен пытаться прочитать JSON.
        await api.UpdateProfileAsync(Guid.Parse(UserId), "Ева", "Клиентова", null, null);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("phoneNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("avatarUrl").ValueKind);
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidationError_ThrowsWithCodeAndStatus()
    {
        const string problemJson = """
            {
              "title": "Validation failed",
              "status": 400,
              "errorCodes": ["user.updateProfile.firstName"],
              "errors": { "firstName": ["Имя обязательно."] }
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.UpdateProfileAsync(Guid.Parse(UserId), "  ", "Клиентова", null, null));

        // По коду экран подсвечивает поле, а detail с сервера английский и контрактом не является.
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("user.updateProfile.firstName", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidPasswords_SendsPostWithBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        await api.ChangePasswordAsync("old-password", "new-password-1!");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/auth/change-password", handler.LastRequestUri!.AbsolutePath);

        // Пользователь берётся из токена: в теле только пара паролей, без id и email.
        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal("old-password", root.GetProperty("currentPassword").GetString());
        Assert.Equal("new-password-1!", root.GetProperty("newPassword").GetString());

        var parsed = JsonSerializer.Deserialize<ChangePasswordRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("old-password", parsed.CurrentPassword);
        Assert.Equal("new-password-1!", parsed.NewPassword);
    }

    [Fact]
    public async Task ChangePasswordAsync_InvalidCurrentPassword_ThrowsWithCode()
    {
        const string problemJson = """
            {
              "title": "Bad Request",
              "status": 400,
              "detail": "The current password is invalid.",
              "errorCodes": ["auth.changePassword.invalidCurrentPassword"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.ChangePasswordAsync("wrong", "new-password-1!"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains(
            "auth.changePassword.invalidCurrentPassword",
            exception.ErrorCodes,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_ValidRequest_SendsPostWithBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        await api.RequestEmailChangeAsync("new@barbersearch.test", "current-password");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/auth/email-change", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal("new@barbersearch.test", root.GetProperty("newEmail").GetString());
        Assert.Equal("current-password", root.GetProperty("currentPassword").GetString());
    }

    [Fact]
    public async Task RequestEmailChangeAsync_Cooldown_ThrowsWithCodeAndStatus()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "Email change was requested too recently.",
              "errorCodes": ["auth.emailChange.cooldown"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.RequestEmailChangeAsync("new@barbersearch.test", "current-password"));

        // Кулдаун — не ошибка валидации: экран показывает «попробуйте позже», а не «письмо ушло».
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("auth.emailChange.cooldown", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RequestEmailVerificationAsync_NoParameters_SendsEmptyPost()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        await api.RequestEmailVerificationAsync();

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/auth/email-verification", handler.LastRequestUri!.AbsolutePath);

        // Адрес сервер берёт из токена: тело запроса не нужно и не должно отправляться.
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task RequestEmailVerificationAsync_Unavailable_ThrowsWithCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "errorCodes": ["auth.emailVerification.unavailable"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.RequestEmailVerificationAsync());

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains(
            "auth.emailVerification.unavailable",
            exception.ErrorCodes,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Ответ <c>GET api/users/me</c>: телефон не задан, аватар есть.
    /// </summary>
    /// <param name="lastLogin">JSON-значение последнего входа: инстант либо <c>null</c>.</param>
    private static string ProfileJson(string lastLogin) => $$"""
        {
          "id": "{{UserId}}",
          "email": "mobile-test@barbersearch.test",
          "firstName": "Ева",
          "lastName": "Клиентова",
          "phoneNumber": null,
          "avatarUrl": "{{AvatarUrl}}",
          "role": "Customer",
          "barberProfileId": null,
          "isEmailVerified": true,
          "isPhoneVerified": false,
          "createdAt": "2026-01-10T09:00:00+00:00",
          "lastLoginAt": {{lastLogin}},
          "isAvatarBlocked": false,
          "avatarModerationStatus": "Approved",
          "avatarRejectionReason": null
        }
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    /// <remarks>
    /// Тело ответа необязательно: у <c>204</c> содержимого нет, и клиент не должен его читать.
    /// </remarks>
    private sealed class StubHandler(HttpStatusCode statusCode, string? body = null, string contentType = "application/json")
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            };
        }
    }
}
