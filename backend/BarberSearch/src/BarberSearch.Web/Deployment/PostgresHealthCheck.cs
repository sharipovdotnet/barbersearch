using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Readiness-проверка PostgreSQL.
/// </summary>
/// <remarks>
/// Проверка создаёт собственный scope: <see cref="ApplicationDbContext"/> scoped, а health check
/// резолвится из корневого провайдера. <c>CanConnectAsync</c> не бросает на недоступной БД,
/// поэтому отсутствие соединения превращается в <c>Unhealthy</c>, а не в 500.
/// Таймаут задаётся при регистрации (<c>AddCheck</c>) и приходит сюда токеном отмены.
/// </remarks>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PostgresHealthCheck(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check timed out.");
        }
        catch (Exception)
        {
            // Детали исключения не попадают в ответ: в строке подключения может быть пароль.
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.");
        }
    }
}
