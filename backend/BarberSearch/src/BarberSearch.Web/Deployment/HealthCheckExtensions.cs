using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Регистрация и разметка health-проверок.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Тег проверки, которая должна пройти для readiness.
    /// </summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Подключает проверки зависимостей.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="databaseTimeoutSeconds">Таймаут проверки БД в секундах.</param>
    public static IServiceCollection AddAppHealthChecks(
        this IServiceCollection services,
        int databaseTimeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(
                "postgres",
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(databaseTimeoutSeconds));

        return services;
    }

    /// <summary>
    /// Маппит <c>/health/live</c> и <c>/health/ready</c>.
    /// </summary>
    /// <remarks>
    /// Liveness не зависит от БД: процесс должен перезапускаться только когда он мёртв,
    /// а не когда недоступна база. Readiness проверяет PostgreSQL и снимает под с балансировщика.
    /// Оба эндпоинта анонимные и не попадают под fallback-политику.
    /// </remarks>
    public static IEndpointRouteBuilder MapAppHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false,
                ResponseWriter = HealthCheckResponseWriter.WriteAsync
            })
            .AllowAnonymous();

        endpoints
            .MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains(ReadyTag),
                ResponseWriter = HealthCheckResponseWriter.WriteAsync
            })
            .AllowAnonymous();

        return endpoints;
    }
}
