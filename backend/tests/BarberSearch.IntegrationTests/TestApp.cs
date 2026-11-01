using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.Csrf;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Тестовый хост API: настоящий конвейер ASP.NET Core в памяти плюс отдельная база данных.
/// </summary>
/// <remarks>
/// Хост создаётся на каждый тест. Это не расточительность, а требование изоляции: rate limiter
/// живёт в памяти инстанса, а <c>TestServer</c> не заполняет <c>RemoteIpAddress</c>, поэтому все
/// запросы попадают в партицию <c>unknown</c> (auth — 10 запросов в минуту). Один общий хост
/// на весь прогон упёрся бы в этот лимит на втором-третьем сценарии.
/// <para>
/// Обязательные настройки (<c>ConnectionStrings:Postgres</c>, <c>Auth:SigningKey</c>,
/// <c>DataProtection:KeysPath</c>) фикстура выставляет переменными окружения до старта хоста:
/// в minimal hosting <c>WebApplication.CreateBuilder</c> читает конфигурацию раньше, чем
/// применяются <c>ConfigureAppConfiguration</c> из <c>WebApplicationFactory</c>, поэтому
/// in-memory источник там уже не виден <c>StartupSettings</c>. Окружение — <c>Testing</c>.
/// Провайдеры логирования очищаются: на Windows хост добавляет EventLog, а запись warning
/// в песочнице падает <c>Win32Exception (5)</c> и превращает первый запрос в <c>500</c>.
/// </para>
/// </remarks>
public sealed class TestApp : IAsyncDisposable
{
    /// <summary>
    /// Пароль тестовых пользователей: удовлетворяет политике (12+ символов, не из словаря).
    /// </summary>
    public const string Password = "Integration!Passw0rd";

    /// <summary>
    /// Ключ подписи JWT для тестового хоста: не короче 32 символов.
    /// </summary>
    public const string SigningKey = "barbersearch-integration-tests-signing-key-2026";

    private readonly WebApplicationFactory<Program> _factory;

    private TestApp(WebApplicationFactory<Program> factory, HttpClient client)
    {
        _factory = factory;
        Client = client;
    }

    /// <summary>
    /// HTTP-клиент хоста. Редиректы не выполняются: статус проверяется как есть.
    /// </summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Сервисы хоста — для сидирования через <c>ApplicationDbContext</c>.
    /// </summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>
    /// Очищает базу и поднимает новый хост с собственной конфигурацией.
    /// </summary>
    public static async Task<TestApp> StartAsync(PostgresFixture database)
    {
        ArgumentNullException.ThrowIfNull(database);

        await database.ResetAsync();

        var factory = new IntegrationWebApplicationFactory();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });
        client.Timeout = TimeSpan.FromSeconds(60);

        // R-50: стартовая уборка refresh-токенов идёт параллельно запуску хоста. Ждём её
        // завершения, иначе DELETE может забрать строки, засеянные тестом, раньше, чем тест
        // вызовет CleanupOnceAsync, и проверка счётчика станет флейкой.
        var cleanup = factory.Services.GetRequiredService<RefreshTokenCleanupService>();
        await cleanup.InitialCleanupCompleted.WaitAsync(TimeSpan.FromSeconds(30));

        return new TestApp(factory, client);
    }

    /// <summary>
    /// Настройка тестового хоста: окружение <c>Testing</c> и чистые провайдеры логирования.
    /// </summary>
    private sealed class IntegrationWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment("Testing");

            // EventLog на Windows падает в песочнице на первой же warning-записи (AGENT.md §5.2).
            // ClearProviders убирает и его, и консоль: вывод тестов остаётся чистым.
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }

    /// <summary>
    /// Создаёт пользователя через доменную фабрику и сохраняет его.
    /// </summary>
    public async Task<User> CreateUserAsync(
        string email,
        UserRole role = UserRole.Customer,
        string password = Password)
    {
        var hasher = new PasswordHasher<User>();
        var created = User.Create(
            email,
            hasher.HashPassword(null!, password),
            "Integration",
            "Test",
            role);

        if (created.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot seed user '{email}': {string.Join("; ", created.Error.Errors.Select(error => error.Code))}");
        }

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(created.Value);
        await db.SaveChangesAsync();

        return created.Value;
    }

    /// <summary>
    /// Регистрирует пользователя через публичный эндпоинт.
    /// </summary>
    public async Task<Guid> RegisterAsync(string email, string password = Password)
    {
        using var response = await Client.PostAsJsonAsync(
            "api/auth/register",
            new RegisterRequest(email, password, "Integration", "Test"));

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    /// <summary>
    /// Входит через <c>POST api/auth/login</c> и возвращает bearer-токен и refresh-токен.
    /// </summary>
    public async Task<TokenResponse> LoginAsync(string email, string password = Password)
    {
        using var response = await Client.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest(email, password));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);

        return body;
    }

    /// <summary>
    /// Вход и заголовок <c>Authorization: Bearer …</c> на этом же клиенте.
    /// </summary>
    public async Task AuthenticateAsync(string email, string password = Password)
    {
        var tokens = await LoginAsync(email, password);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    /// <summary>
    /// Браузерная сессия: отдельный клиент с cookie-контейнером и свежим antiforgery-токеном.
    /// </summary>
    /// <remarks>
    /// Базовый адрес — <c>https://localhost</c>: в <c>Testing</c> все cookie <c>Secure</c>, и
    /// контейнер не отправил бы их обратно по http. TLS <c>TestServer</c> не терминирует —
    /// схема нужна только для <c>Request.IsHttps</c> и решений cookie-контейнера.
    /// <para>
    /// Токен берётся повторно **после** входа: antiforgery-токен привязан к личности, и
    /// анонимный токен на первой мутации дал бы <c>400 auth.csrf.invalid</c>.
    /// </para>
    /// </remarks>
    public async Task<CookieSession> SignInWithCookieAsync(
        string email,
        bool rememberMe = false,
        string password = Password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.Timeout = TimeSpan.FromSeconds(60);

        var loginToken = await GetCsrfTokenAsync(client);

        using (var loginRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/login/cookie")
        {
            Content = JsonContent.Create(new LoginRequest(email, password, rememberMe))
        })
        {
            loginRequest.Headers.Add(AuthConstants.CSRF_HEADER_NAME, loginToken);

            using var loginResponse = await client.SendAsync(loginRequest);

            if (loginResponse.StatusCode != HttpStatusCode.NoContent)
            {
                var detail = await loginResponse.Content.ReadAsStringAsync();
                client.Dispose();

                throw new InvalidOperationException(
                    $"Cookie sign-in for '{email}' failed: {loginResponse.StatusCode} {detail}");
            }
        }

        var csrfToken = await GetCsrfTokenAsync(client);

        return new CookieSession(client, csrfToken);
    }

    /// <summary>
    /// Выдаёт antiforgery-токен для cookie-контейнера клиента (и ставит его cookie).
    /// </summary>
    public static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync("api/auth/csrf");

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"GET api/auth/csrf failed: {response.StatusCode}");
        }

        var body = await response.Content.ReadFromJsonAsync<CsrfResponse>();

        if (body is null || string.IsNullOrWhiteSpace(body.Token))
        {
            throw new InvalidOperationException("GET api/auth/csrf returned an empty token.");
        }

        return body.Token;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}
