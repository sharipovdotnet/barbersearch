using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.HairServices;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Application.Reviews;
using BarberSearch.Application.Salons;
using BarberSearch.Application.Specializations;
using BarberSearch.Application.Users;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Infrastructure.Postgresql;

/// <summary>
/// Регистрация инфраструктурного слоя PostgreSQL в DI-контейнере.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Подключает <see cref="ApplicationDbContext"/> к PostgreSQL.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="connectionString">
    /// Строка подключения к PostgreSQL. Передаётся снаружи, чтобы инфраструктурный слой
    /// ничего не знал о том, откуда берётся конфигурация.
    /// </param>
    /// <returns>Та же коллекция сервисов — для цепочки вызовов.</returns>
    /// <remarks>
    /// Используется <c>AddDbContextPool</c>: контекст не хранит состояния на время запроса,
    /// поэтому экземпляры можно переиспользовать и не создавать по одному на каждый запрос.
    /// </remarks>
    public static IServiceCollection AddPostgresInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextPool<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }

    /// <summary>
    /// Регистрирует реализации репозиториев из <c>Repositories</c>.
    /// </summary>
    /// <remarks>
    /// Регистрируется отдельно от <c>AddPostgresInfrastructure</c>: состав сервисов остаётся
    /// явным, а композиционный корень (слой Web) вызывает оба метода сам. Так репозитории можно
    /// подключить и без контекста — например, в интеграционных тестах со своим <c>DbContext</c>.
    /// <para>
    /// Время жизни — <c>Scoped</c>: репозиторий держит <see cref="ApplicationDbContext"/>,
    /// который живёт в границах одного запроса. Публичные ключи генерирует домен (UUIDv7),
    /// поэтому отдельный метод «сохранить и получить Id» не нужен.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBarberProfileRepository, BarberProfileRepository>();
        services.AddScoped<ISalonRepository, SalonRepository>();
        services.AddScoped<IHairServiceRepository, HairServiceRepository>();
        services.AddScoped<ISpecializationRepository, SpecializationRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        return services;
    }
}
