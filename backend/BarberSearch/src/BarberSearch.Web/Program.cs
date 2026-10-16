using BarberSearch.Web;

var builder = WebApplication.CreateBuilder(args);

// Композиционный корень: PostgreSQL и репозитории подключаются через DependencyInjection слоя Web,
// строка подключения берётся из конфигурации — ConnectionStrings:Postgres.
builder.Services.AddProgramDependencies(builder.Configuration);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();