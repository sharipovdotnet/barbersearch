using BarberSearch.Application.Emails;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет валидацию конфигурации в <see cref="StartupSettings"/>: fail-closed
/// <c>AllowedHosts</c> (R-47) и диапазоны секции <c>EmailOutbox</c>.
/// </summary>
/// <remarks>
/// Хост не запускается и база не нужна: проверяется только чтение конфигурации. Каталог
/// Data Protection указывает на свежий временный каталог, потому что проверка пути key ring
/// выполняется раньше проверки <c>AllowedHosts</c> и требует записываемого каталога.
/// </remarks>
public class StartupSettingsTests
{
    [Fact]
    public void Load_InProduction_RejectsWildcardAllowedHosts()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Production, new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*"
            }, keysPath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => StartupSettings.Load(builder.Configuration, builder.Environment));

            Assert.Contains("AllowedHosts", exception.Message, StringComparison.Ordinal);
            Assert.Contains("'*' is not allowed", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithEmptyEmailOutboxSection_UsesDefaults()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, [], keysPath);

            var settings = StartupSettings.Load(builder.Configuration, builder.Environment);

            Assert.False(settings.EmailOutbox.Enabled);
            Assert.Equal(EmailOutboxOptions.DEFAULT_BATCH_SIZE, settings.EmailOutbox.BatchSize);
            Assert.Equal(EmailOutboxOptions.DEFAULT_INTERVAL_SECONDS, settings.EmailOutbox.IntervalSeconds);
            Assert.Equal(EmailOutboxOptions.DEFAULT_MAX_ATTEMPTS, settings.EmailOutbox.MaxAttempts);
            Assert.Equal(EmailOutboxOptions.DEFAULT_RETENTION_DAYS, settings.EmailOutbox.RetentionDays);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithEmailOutboxSection_ReadsValues()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                ["EmailOutbox:Enabled"] = "true",
                ["EmailOutbox:BatchSize"] = "50",
                ["EmailOutbox:IntervalSeconds"] = "15",
                ["EmailOutbox:MaxAttempts"] = "3",
                ["EmailOutbox:RetentionDays"] = "14"
            }, keysPath);

            var settings = StartupSettings.Load(builder.Configuration, builder.Environment);

            Assert.True(settings.EmailOutbox.Enabled);
            Assert.Equal(50, settings.EmailOutbox.BatchSize);
            Assert.Equal(15, settings.EmailOutbox.IntervalSeconds);
            Assert.Equal(3, settings.EmailOutbox.MaxAttempts);
            Assert.Equal(14, settings.EmailOutbox.RetentionDays);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Theory]
    [InlineData("EmailOutbox:BatchSize", "0")]
    [InlineData("EmailOutbox:BatchSize", "1001")]
    [InlineData("EmailOutbox:BatchSize", "many")]
    [InlineData("EmailOutbox:IntervalSeconds", "0")]
    [InlineData("EmailOutbox:IntervalSeconds", "3601")]
    [InlineData("EmailOutbox:MaxAttempts", "0")]
    [InlineData("EmailOutbox:MaxAttempts", "101")]
    [InlineData("EmailOutbox:RetentionDays", "0")]
    [InlineData("EmailOutbox:RetentionDays", "366")]
    public void Load_WithInvalidEmailOutboxValue_Throws(string key, string value)
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                [key] = value
            }, keysPath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => StartupSettings.Load(builder.Configuration, builder.Environment));

            Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    private static WebApplicationBuilder CreateBuilder(
        string environment,
        Dictionary<string, string?> overrides,
        string keysPath)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment
        });

        // appsettings*.json из Web копируются в вывод тестов; здесь нужна только
        // конфигурация теста, иначе «пустая секция» зависела бы от файла приложения.
        builder.Configuration.Sources.Clear();

        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] =
                "Host=127.0.0.1;Port=59999;Database=missing;Username=postgres;Password=postgres;Timeout=1",
            ["Auth:SigningKey"] = new string('s', AuthOptions.MIN_SIGNING_KEY_LENGTH),
            ["Auth:Issuer"] = "BarberSearch",
            ["Auth:Audience"] = "BarberSearch.Api",
            ["Auth:AccessTokenMinutes"] = "15",
            ["Auth:RefreshTokenDays"] = "14",
            ["Auth:CookieHours"] = "8",
            ["Auth:CookieSameSite"] = "Lax",
            ["Auth:ClockSkewSeconds"] = "30",
            ["DataProtection:KeysPath"] = keysPath
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(values);

        return builder;
    }

    private static string CreateKeysPath()
    {
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            "barbersearch-startup-settings-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(keysPath);

        return keysPath;
    }

    private static void DeleteKeysPath(string keysPath)
    {
        if (Directory.Exists(keysPath))
        {
            Directory.Delete(keysPath, recursive: true);
        }
    }
}

