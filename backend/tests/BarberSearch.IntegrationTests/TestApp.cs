using System.Net.Http.Json;
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}
