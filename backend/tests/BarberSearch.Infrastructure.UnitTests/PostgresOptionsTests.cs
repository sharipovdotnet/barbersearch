using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет настройки Npgsql, которые задаются в DI, без подключения к базе (R-18).
/// </summary>
public class PostgresOptionsTests
{
    [Fact]
    public void AddPostgresInfrastructure_ConfiguresCommandTimeout()
    {
        var services = new ServiceCollection();
        services.AddPostgresInfrastructure(
            "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test");
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        var relational = Assert.Single(options.Extensions.OfType<RelationalOptionsExtension>());

        Assert.Equal(PostgresSettings.COMMAND_TIMEOUT_SECONDS, relational.CommandTimeout);
    }
}
