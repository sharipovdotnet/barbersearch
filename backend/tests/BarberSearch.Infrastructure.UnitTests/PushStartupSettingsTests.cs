using BarberSearch.Application.Push;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет валидацию секции <c>Push</c> в <see cref="StartupSettings"/>: диапазоны чисел
/// и fail-fast вместо тихого значения по умолчанию.
/// </summary>
/// <remarks>
/// Секрет не проверяется: пустые <c>ProjectId</c> и ключи — штатное состояние, в котором
/// приложение обязано собираться и работать. Хост не запускается, база не нужна.
/// </remarks>
public class PushStartupSettingsTests
{
    [Fact]
    public void Load_WithEmptyPushSection_UsesDefaults()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, [], keysPath);

            var settings = StartupSettings.Load(builder.Configuration, builder.Environment);

            Assert.False(settings.Push.Enabled);
            Assert.Equal(PushOptions.DEFAULT_INTERVAL_SECONDS, settings.Push.IntervalSeconds);
            Assert.Equal(PushOptions.DEFAULT_BATCH_SIZE, settings.Push.BatchSize);
            Assert.Equal(
                PushOptions.DEFAULT_REMINDER_LEAD_MINUTES,
                settings.Push.ReminderLeadMinutes);
            Assert.Equal(PushOptions.DEFAULT_MAX_ATTEMPTS, settings.Push.MaxAttempts);
            Assert.Equal(PushOptions.DEFAULT_RETENTION_DAYS, settings.Push.RetentionDays);
            Assert.Equal(
                PushOptions.DEFAULT_DEVICE_TOKEN_RETENTION_DAYS,
                settings.Push.DeviceTokenRetentionDays);
            Assert.Equal(
                PushOptions.DEFAULT_MAX_TOKENS_PER_USER,
                settings.Push.MaxTokensPerUser);
            Assert.False(settings.Push.IsConfigured);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithPushSection_ReadsValues()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                ["Push:Enabled"] = "true",
                ["Push:IntervalSeconds"] = "15",
                ["Push:BatchSize"] = "50",
                ["Push:ReminderLeadMinutes"] = "90",
                ["Push:MaxAttempts"] = "3",
                ["Push:RetentionDays"] = "14",
                ["Push:DeviceTokenRetentionDays"] = "30",
                ["Push:MaxTokensPerUser"] = "2",
                ["Push:ProjectId"] = "demo-project"
            }, keysPath);

            var settings = StartupSettings.Load(builder.Configuration, builder.Environment);

            Assert.True(settings.Push.Enabled);
            Assert.Equal(15, settings.Push.IntervalSeconds);
            Assert.Equal(50, settings.Push.BatchSize);
            Assert.Equal(90, settings.Push.ReminderLeadMinutes);
            Assert.Equal(3, settings.Push.MaxAttempts);
            Assert.Equal(14, settings.Push.RetentionDays);
            Assert.Equal(30, settings.Push.DeviceTokenRetentionDays);
            Assert.Equal(2, settings.Push.MaxTokensPerUser);
            Assert.Equal("demo-project", settings.Push.ProjectId);
            // Секрета нет — канал не настроен; это не ошибка старта.
            Assert.False(settings.Push.IsConfigured);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithLeadShorterThanTheTickInterval_Throws()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                ["Push:IntervalSeconds"] = "120",
                ["Push:ReminderLeadMinutes"] = "1"
            }, keysPath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => StartupSettings.Load(builder.Configuration, builder.Environment));

            Assert.Contains("Push:ReminderLeadMinutes", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Theory]
    [InlineData("Push:IntervalSeconds", "0")]
    [InlineData("Push:IntervalSeconds", "3601")]
    [InlineData("Push:BatchSize", "0")]
    [InlineData("Push:BatchSize", "1001")]
    [InlineData("Push:MaxAttempts", "0")]
    [InlineData("Push:MaxAttempts", "101")]
    [InlineData("Push:ReminderLeadMinutes", "0")]
    [InlineData("Push:ReminderLeadMinutes", "1441")]
    [InlineData("Push:RetentionDays", "0")]
    [InlineData("Push:RetentionDays", "366")]
    [InlineData("Push:DeviceTokenRetentionDays", "0")]
    [InlineData("Push:DeviceTokenRetentionDays", "366")]
    [InlineData("Push:MaxTokensPerUser", "0")]
    [InlineData("Push:MaxTokensPerUser", "101")]
    [InlineData("Push:BatchSize", "many")]
    public void Load_WithInvalidPushValue_Throws(string key, string value)
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
            "barbersearch-push-startup-settings-tests",
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
