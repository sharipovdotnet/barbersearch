using BarberSearch.Application;
using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Web.Auth;
using Microsoft.AspNetCore.Identity;

namespace BarberSearch.Web;

/// <summary>
/// Композиционный корень приложения: единственное место, где слои связываются между собой.
/// </summary>
/// <remarks>
/// Класс ничего не создаёт сам — он получает уже проверенную строку подключения и передаёт
/// регистрации в слой <c>BarberSearch.Infrastructure.Postgresql</c>. Program.cs благодаря этому
/// не знает ни про <c>ApplicationDbContext</c>, ни про конкретные репозитории.
/// Здесь же регистрируется хешер паролей: хеширование — задача слоя Web
/// (см. <c>CreateUserCommand.PasswordHash</c>), поэтому зависимость нужна именно ему.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Имя строки подключения в конфигурации: <c>ConnectionStrings:Postgres</c>.
    /// </summary>
    public const string POSTGRES_CONNECTION_STRING_NAME = "Postgres";

    /// <summary>
    /// Подключает PostgreSQL и регистрирует репозитории.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="connectionString">
    /// Проверенная строка подключения. Читается в <c>StartupSettings</c> из
    /// <c>ConnectionStrings:Postgres</c> или <c>ConnectionStrings__Postgres</c>.
    /// </param>
    /// <returns>Та же коллекция сервисов — для цепочки вызовов.</returns>
    public static IServiceCollection AddProgramDependencies(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Хешер без состояния и без зависимостей — singleton, как и в ASP.NET Core Identity.
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        // Текущий пользователь запроса: Application видит только абстракцию, а HttpContext
        // читается здесь, в Web, — слой Application не знает про ASP.NET Core.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services.AddPostgresInfrastructure(connectionString)
            .AddRepositories()
            .AddApplication();
    }
}
