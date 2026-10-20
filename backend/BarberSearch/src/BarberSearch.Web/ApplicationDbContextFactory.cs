using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarberSearch.Web;

/// <summary>
/// Design-time контекст для <c>dotnet ef</c>.
/// </summary>
/// <remarks>
/// Миграции не должны зависеть от runtime-конфигурации деплоя: ключ подписи и путь к key ring
/// при <c>dotnet ef</c> не нужны. Фабрика берёт только строку подключения из переменной
/// окружения <c>ConnectionStrings__Postgres</c>; так команда работает и без запуска
/// <c>Program.cs</c> с его fail-fast проверками.
/// </remarks>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <summary>
    /// Создаёт контекст для миграций.
    /// </summary>
    /// <param name="args">Аргументы команды; не используются.</param>
    /// <exception cref="InvalidOperationException">Строка подключения не задана.</exception>
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                """
                ConnectionStrings__Postgres is not set.
                dotnet ef uses this design-time factory instead of Program.cs; export the
                connection string before running migrations (the same variable the API uses).
                """);
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
