using System.Text.Json.Serialization;
using BarberSearch.Web;

var builder = WebApplication.CreateBuilder(args);

// Композиционный корень: PostgreSQL и репозитории подключаются через DependencyInjection слоя Web,
// строка подключения берётся из конфигурации — ConnectionStrings:Postgres.
builder.Services.AddProgramDependencies(builder.Configuration);

// Add services to the container.

// Перечисления в JSON — строками: Grade, Language, PaymentMethod и UserRole читаются из тела
// по имени, а не по числу, и так же попадают в схему OpenAPI.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Ответы об ошибках в формате ProblemDetails: контроллеры отдают их сами (см. Web/Common),
// а этот сервис отвечает за необработанные исключения.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI читает документ, который построил AddOpenApi, — отдельная генерация не нужна.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "BarberSearch API v1"));
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
