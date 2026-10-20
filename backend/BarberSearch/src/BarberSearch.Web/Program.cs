using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BarberSearch.Web;
using BarberSearch.Web.Auth;
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

// При остановке хоста Kestrel перестаёт принимать новые соединения и ждёт in-flight запросы
// не дольше этого таймаута. Оркестратор должен давать pod'у не меньше времени.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(startup.ShutdownTimeoutSeconds));

// /health/live не трогает БД, /health/ready проверяет PostgreSQL.
builder.Services.AddAppHealthChecks(startup.HealthDatabaseTimeoutSeconds);

// Перечисления в JSON — строками: Grade, Language, PaymentMethod и UserRole читаются из тела
// по имени, а не по числу, и так же попадают в схему OpenAPI.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Ответы об ошибках в формате ProblemDetails: контроллеры отдают их сами (см. Web/Common),
// а этот сервис отвечает за необработанные исключения и за 401/403 middleware аутентификации.
builder.Services.AddProblemDetails();

// Перебор паролей не должен быть бесплатным: 10 попыток в минуту на адрес.
// Адрес берётся из Connection.RemoteIpAddress уже после UseForwardedHeaders.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

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

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

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
app.UseRateLimiter();

app.MapControllers();
app.MapAppHealthChecks();

app.Run();
