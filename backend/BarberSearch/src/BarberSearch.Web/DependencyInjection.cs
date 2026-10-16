using BarberSearch.Application;
using BarberSearch.Infrastructure.Postgresql;

namespace BarberSearch.Web;

/// <summary>
/// Композиционный корень приложения: единственное место, где слои связываются между собой.
/// </summary>
/// <remarks>
/// Класс ничего не создаёт сам — он достаёт строку подключения из конфигурации и передаёт
/// регистрации в слой <c>BarberSearch.Infrastructure.Postgresql</c>. Program.cs благодаря этому
/// не знает ни про <c>ApplicationDbContext</c>, ни про конкретные репозитории.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Имя строки подключения в конфигурации: <c>ConnectionStrings:Postgres</c>.
    /// </summary>
    public const string POSTGRES_CONNECTION_STRING_NAME = "Postgres";

    /// <summary>
    /// Подключает PostgreSQL: читает строку подключения из конфигурации и передаёт её
    /// в <c>AddPostgresInfrastructure</c> инфраструктурного слоя, а также добавляет репозитории.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="configuration">
    /// Конфигурация приложения. Ожидается ключ <c>ConnectionStrings:Postgres</c>; в Development
    /// его можно переопределить через user secrets, в проде — через переменную окружения
    /// <c>ConnectionStrings__Postgres</c>.
    /// </param>
    /// <returns>Та же коллекция сервисов — для цепочки вызовов.</returns>
    /// <exception cref="InvalidOperationException">
    /// Строка подключения не задана ни в одном источнике конфигурации.
    /// </exception>
    public static IServiceCollection AddProgramDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(POSTGRES_CONNECTION_STRING_NAME);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"""
                 Connection string '{POSTGRES_CONNECTION_STRING_NAME}' is not configured.
                 Set 'ConnectionStrings:{POSTGRES_CONNECTION_STRING_NAME}' in appsettings.json,
                 user secrets, or the environment variable
                 'ConnectionStrings__{POSTGRES_CONNECTION_STRING_NAME}'.
                 """);
        }

        return services.AddPostgresInfrastructure(connectionString)
            .AddRepositories()
            .AddApplication();
    }
}
