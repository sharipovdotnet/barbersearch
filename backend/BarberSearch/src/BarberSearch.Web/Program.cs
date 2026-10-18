using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BarberSearch.Web;
using BarberSearch.Web.Auth;
using BarberSearch.Web.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Композиционный корень: PostgreSQL и репозитории подключаются через DependencyInjection слоя Web,
// строка подключения берётся из конфигурации — ConnectionStrings:Postgres.
builder.Services.AddProgramDependencies(builder.Configuration);

// Аутентификация: cookie для браузера и bearer для остальных клиентов, плюс политики авторизации.
builder.Services
    .AddAppAuthentication(builder.Configuration, builder.Environment)
    .AddAppAuthorization();

// Перечисления в JSON — строками: Grade, Language, PaymentMethod и UserRole читаются из тела
// по имени, а не по числу, и так же попадают в схему OpenAPI.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Ответы об ошибках в формате ProblemDetails: контроллеры отдают их сами (см. Web/Common),
// а этот сервис отвечает за необработанные исключения и за 401/403 middleware аутентификации.
builder.Services.AddProblemDetails();

// Перебор паролей не должен быть бесплатным: 10 попыток в минуту на адрес.
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

// CORS нужен только если браузерный клиент живёт на другом origin. Пустой список источников
// означает, что политика не регистрируется вовсе.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("spa", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // Fallback-политика закрывает всё, что не помечено явно, поэтому документ и UI
    // открываются анонимно — иначе Swagger не загрузится.
    app.MapOpenApi().AllowAnonymous();

    // Swagger UI читает документ, который построил AddOpenApi, — отдельная генерация не нужна.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "BarberSearch API v1"));
}

if (allowedOrigins.Length > 0)
{
    app.UseCors("spa");
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();
