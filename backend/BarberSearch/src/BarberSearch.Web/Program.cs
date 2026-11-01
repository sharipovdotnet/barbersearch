using BarberSearch.Web;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using BarberSearch.Web.OpenApi;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Конфигурация читается и проверяется один раз, до регистрации сервисов: отсутствие ключа
// подписи, пути key ring или доверенного прокси должно ронять старт, а не первый запрос.
var startup = StartupSettings.Load(builder.Configuration, builder.Environment);

// Композиционный корень: PostgreSQL и репозитории подключаются через DependencyInjection слоя Web.
builder.Services.AddProgramDependencies(startup.PostgresConnectionString);

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

/// <summary>
/// Тестовый вход: <c>WebApplicationFactory&lt;Program&gt;</c> поднимает этот же хост в памяти.
/// </summary>
public partial class Program
{
}
