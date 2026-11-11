using System.Globalization;
using BarberSearch.Application.Emails;
using BarberSearch.Application.Notifications;
using BarberSearch.Infrastructure.Email;
using BarberSearch.Infrastructure.Telegram;
using BarberSearch.Web;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using BarberSearch.Web.Emails;
using BarberSearch.Web.Notifications;
using BarberSearch.Web.OpenApi;
using BarberSearch.Web.Storage;
using Microsoft.Extensions.Hosting;
using Serilog;

// Bootstrap-логгер пишет в консоль, пока хост не собран: упавшая проверка конфигурации ниже
// должна оставить след в выводе, а не только в стеке исключения. Хост заменяет его логгером
// из Logging:File (UseSerilog), а finally закрывает и сбрасывает файл при остановке.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Конфигурация читается и проверяется один раз, до регистрации сервисов: отсутствие ключа
    // подписи, пути key ring или доверенного прокси должно ронять старт, а не первый запрос.
    var startup = StartupSettings.Load(builder.Configuration, builder.Environment);

    // Serilog — единственный логгер приложения: консоль и файл. Уровни берутся из
    // Logging:LogLevel (R-13), путь и ротация файла — из Logging:File; каталог проверяется
    // здесь же, до старта хоста. В тестовом хосте Serilog не регистрируется: WebApplicationFactory
    // очищает провайдеры логирования, и прогон не должен писать файлы
    // (см. tests/BarberSearch.IntegrationTests/TestApp.cs).
    var useSerilog = !builder.Environment.IsEnvironment("Testing");

    if (useSerilog)
    {
        builder.UseAppSerilog(
            FileLoggingSettings.FromConfiguration(builder.Configuration, builder.Environment));
    }

    // Композиционный корень: PostgreSQL и репозитории подключаются через DependencyInjection слоя Web.
    builder.Services.AddProgramDependencies(startup.PostgresConnectionString);

    // Локальное хранилище фотографий портфолио. Настройки разрешаются, а каталог создаётся и
    // проверяется на запись прямо здесь: недоступный диск должен ронять старт, а не первую
    // загрузку. Регистрация обязательна и для тестового хоста — обработчики Application
    // зависят от IPhotoStorage.
    builder.Services.AddPhotoStorage(builder.Configuration, builder.Environment);

    // Настройки напоминаний читаются один раз и живут синглтоном: Application не знает
    // ни про IConfiguration, ни про IOptions. Пустые секции дают значения по умолчанию
    // (воркер выключен, Telegram не настроен), поэтому отсутствие ключей не роняет старт.
    builder.Services.AddSingleton(
        builder.Configuration.GetSection(RemindersOptions.SECTION).Get<RemindersOptions>()
        ?? new RemindersOptions());
    builder.Services.AddSingleton(
        builder.Configuration.GetSection(AppOptions.SECTION).Get<AppOptions>()
        ?? new AppOptions());

    // Почта: пустая секция — нормальное состояние (письма не уходят, регистрация работает).
    // Адрес App:PublicUrl проверяется в обработчике запроса письма, а не здесь.
    builder.Services.AddEmailSender(
        builder.Configuration.GetSection(EmailOptions.SECTION).Get<EmailOptions>()
        ?? new EmailOptions());

    // Outbox писем: настройки уже проверены StartupSettings (диапазоны BatchSize, IntervalSeconds,
    // MaxAttempts, RetentionDays). Выключенный воркер не роняет старт и не теряет письма —
    // обработчики пишут строки всегда, когда почта настроена.
    builder.Services.AddSingleton(startup.EmailOutbox);

    // Воркер outbox. Конкретный тип — синглтон, а хост получает его же через фабрику:
    // интеграционный тест вызывает RunTickAsync у того же экземпляра, не создавая второй
    // фоновый сервис (тот же приём, что у ReminderDispatchService).
    builder.Services.AddSingleton<EmailDispatchService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailDispatchService>());

    // Telegram — единственный внешний HTTP-канал напоминаний; отдельного NuGet-пакета нет.
    // Пустая секция — нормальное состояние: канал выключен, воркер работает без него.
    builder.Services.AddTelegramSender(
        builder.Configuration.GetSection(TelegramOptions.SECTION).Get<TelegramOptions>()
        ?? new TelegramOptions());

    // Воркер напоминаний. Конкретный тип — синглтон, а хост получает его же через фабрику:
    // интеграционный тест вызывает RunTickAsync у того же экземпляра, не создавая второй
    // фоновый сервис (тот же приём, что у RefreshTokenCleanupService).
    builder.Services.AddSingleton<ReminderDispatchService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ReminderDispatchService>());

    // Ключи Data Protection лежат на постоянном томе и общие для всех инстансов.
    builder.Services.AddPersistentDataProtection(startup.DataProtection);

    // Аутентификация: cookie для браузера и bearer для остальных клиентов, плюс политики авторизации.
    builder.Services
        .AddAppAuthentication(startup.Auth, builder.Environment)
        .AddAppAuthorization();

    // R-50: фоновая уборка истёкших refresh-токенов. DELETE идемпотентен, поэтому несколько
    // инстансов могут убирать одновременно; интервал проверен StartupSettings (1..1440 минут).
    builder.Services.AddSingleton(new RefreshTokenCleanupOptions(
        TimeSpan.FromMinutes(startup.RefreshTokenCleanupIntervalMinutes)));

    // Конкретный тип — синглтон, а хост получает его же через фабрику: так интеграционный тест
    // вызывает CleanupOnceAsync у того же экземпляра, а не создаёт второй фоновой сервис.
    builder.Services.AddSingleton<RefreshTokenCleanupService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RefreshTokenCleanupService>());

    // Уборка одноразовых токенов подтверждения email: сразу на старте и далее раз в час.
    // Ретенция (30 дней) и TTL живут в Application (EmailVerificationLifetime).
    builder.Services.AddSingleton<EmailVerificationTokenCleanupService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailVerificationTokenCleanupService>());

    // Тот же порядок для токенов сброса пароля: та же ретенция 30 дней, другой TTL.
    builder.Services.AddSingleton<PasswordResetTokenCleanupService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PasswordResetTokenCleanupService>());

    // Уборка токенов смены адреса email: та же ретенция 30 дней, что у подтверждения email.
    builder.Services.AddSingleton<EmailChangeTokenCleanupService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailChangeTokenCleanupService>());

    // При остановке хоста Kestrel перестаёт принимать новые соединения и ждёт in-flight запросы
    // не дольше этого таймаута. Оркестратор должен давать pod'у не меньше времени.
    builder.Services.Configure<HostOptions>(options =>
        options.ShutdownTimeout = TimeSpan.FromSeconds(startup.ShutdownTimeoutSeconds));

    // /health/live не трогает БД, /health/ready проверяет PostgreSQL.
    builder.Services.AddAppHealthChecks(startup.HealthDatabaseTimeoutSeconds);

    // JSON/текст сжимаются встроенными провайдерами; изображения и уже сжатые форматы
    // в списке MIME отсутствуют.
    builder.Services.AddResponseCompression(ResponseCompressionSettings.Configure);

    // 30 секунд на бизнес-запрос: зависший запрос не должен держать поток до клиентского таймаута.
    builder.Services.AddRequestTimeouts(RequestTimeoutSettings.Configure);

    // Перечисления в JSON — строками: Grade, Language, PaymentMethod и UserRole читаются из тела
    // по имени, а не по числу, и так же попадают в схему OpenAPI. Целые числа отклоняются (R-23).
    // Antiforgery — фильтром на все контроллеры: следующая cookie-мутация не должна остаться
    // без защиты из-за забытого атрибута. Исключения помечены [SkipCookieAntiforgery].
    builder.Services.AddControllers(options =>
            options.Filters.Add<CookieMutationAntiforgeryFilter>())
        .AddJsonOptions(options => JsonSettings.Configure(options.JsonSerializerOptions));

    // Ответы об ошибках в формате ProblemDetails: контроллеры отдают их сами (см. Web/Common),
    // а этот сервис отвечает за необработанные исключения и за 401/403 middleware аутентификации.
    builder.Services.AddProblemDetails();

    // Перебор паролей не должен быть бесплатным: 10 попыток в минуту на адрес. Отдельно
    // ограничена запись — 30 запросов в минуту на пользователя (sub), а без него на адрес:
    // спам POST api/bookings и api/reviews не должен занимать слоты настоящим клиентам.
    // Ответ 429 — ProblemDetails с Retry-After и кодом rateLimit.exceeded.
    builder.Services.AddRateLimiter(RateLimitSettings.Configure);

    // CORS нужен только если браузерный клиент живёт на другом источнике. Список проверен
    // StartupSettings: wildcard запрещён, потому что политика разрешает credentials.
    if (startup.Cors.Enabled)
    {
        builder.Services.AddCors(options => options.AddPolicy(CorsSettings.PolicyName, policy => policy
            .WithOrigins(startup.Cors.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
    }

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi(options =>
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

    var app = builder.Build();

    // Forwarded headers — первыми: от них зависят scheme, RemoteIpAddress и HTTPS-redirect.
    // Middleware не регистрируется, если прокси не настроен, поэтому заголовки от произвольного
    // клиента не принимаются.
    if (startup.ForwardedHeaders.Enabled)
    {
        app.UseForwardedHeaders(startup.ForwardedHeaders.ToOptions());
    }

    // Сжатие — сразу после forwarded headers и до обработчика исключений: оно должно обернуть
    // весь ответ, включая ProblemDetails и статические файлы Swagger UI.
    app.UseResponseCompression();

    // Журнал запросов — тоже до обработчика исключений: UseExceptionHandler повторно выполняет
    // конвейер, и middleware ниже него записал бы один запрос дважды.
    if (useSerilog)
    {
        app.UseAppRequestLogging();
    }

    // Configure the HTTP request pipeline.
    app.UseExceptionHandler();

    // Общий таймаут покрывает весь бизнес-путь: аутентификацию, rate limiter и контроллеры.
    // Health-эндпоинты выведены из него собственной политикой (см. MapAppHealthChecks).
    app.UseRequestTimeouts();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    if (app.Environment.IsDevelopment())
    {
        // Fallback-политика закрывает всё, что не помечено явно, поэтому документ и UI
        // открываются анонимно — иначе Swagger не загрузится.
        app.MapOpenApi().AllowAnonymous();

        // Swagger UI читает документ, который построил AddOpenApi, — отдельная генерация не нужна.
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "BarberSearch API v1"));
    }

    app.UseHttpsRedirection();

    if (startup.Cors.Enabled)
    {
        app.UseCors(CorsSettings.PolicyName);
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Лимитер — строго после аутентификации (R-53): ключ партиции записи — claim sub
    // (RateLimitSettings.WritePartitionKey), поэтому до UseAuthentication его нет. Перенос выше
    // auth сэкономил бы аутентификацию отбрасываемых запросов, но превратил бы лимит 30/мин
    // в общий на IP и сломал бы квоту на пользователя (R-22). Порядок зафиксирован
    // интеграционным тестом RateLimitOrderTests.
    app.UseRateLimiter();

    app.MapControllers();
    app.MapAppHealthChecks();

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Тестовый вход: <c>WebApplicationFactory&lt;Program&gt;</c> поднимает этот же хост в памяти.
/// </summary>
public partial class Program
{
}
