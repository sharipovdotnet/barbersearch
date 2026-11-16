using BarberSearch.Application.Addresses;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет валидацию секции <c>AddressSuggestions</c> в <see cref="StartupSettings"/>:
/// дефолты, fail-fast включённой фичи без ключа, диапазоны чисел и схему <c>BaseUrl</c>.
/// </summary>
/// <remarks>
/// Хост не запускается и база не нужна: проверяется только чтение конфигурации. Выключенная
/// фича с пустым ключом — штатное состояние, поэтому проверки включаются вместе с флагом
/// <c>Enabled=true</c>, а значения секрета никогда не попадают в текст исключения.
/// </remarks>
public class AddressSuggestionsStartupSettingsTests
{
    private const string SecretKey = "integration-test-key";

    [Fact]
    public void Load_WithEmptyAddressSuggestionsSection_UsesDefaults()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, [], keysPath);

            var settings = StartupSettings.Load(builder.Configuration, builder.Environment);

            Assert.False(settings.AddressSuggestions.Enabled);
            Assert.Equal(
                AddressSuggestionsOptions.DEFAULT_BASE_URL,
                settings.AddressSuggestions.BaseUrl);
            Assert.Null(settings.AddressSuggestions.ApiKey);
            Assert.Equal(
                AddressSuggestionsOptions.DEFAULT_TIMEOUT_SECONDS,
                settings.AddressSuggestions.TimeoutSeconds);
            Assert.Equal(
                AddressSuggestionsOptions.DEFAULT_MIN_QUERY_LENGTH,
                settings.AddressSuggestions.MinQueryLength);
            Assert.Equal(
                AddressSuggestionsOptions.DEFAULT_MIN_CITY_QUERY_LENGTH,
                settings.AddressSuggestions.MinCityQueryLength);
            Assert.Equal(
                AddressSuggestionsOptions.DEFAULT_MAX_SUGGESTIONS,
                settings.AddressSuggestions.MaxSuggestions);
            Assert.Equal(
                AddressSuggestionsOptions.DEFAULT_CACHE_TTL_SECONDS,
                settings.AddressSuggestions.CacheTtlSeconds);
            Assert.False(settings.AddressSuggestions.IsConfigured);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithEnabledFeatureAndEmptyKey_ThrowsNamingTheEnvironmentVariable()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                ["AddressSuggestions:Enabled"] = "true",
                ["AddressSuggestions:ApiKey"] = string.Empty
            }, keysPath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => StartupSettings.Load(builder.Configuration, builder.Environment));

            Assert.Contains(
                "AddressSuggestions__ApiKey",
                exception.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(SecretKey, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithEnabledFeatureAndHttpBaseUrlInProduction_Throws()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Production, new Dictionary<string, string?>
            {
                ["AddressSuggestions:Enabled"] = "true",
                ["AddressSuggestions:ApiKey"] = SecretKey,
                ["AddressSuggestions:BaseUrl"] = "http://localhost:9999/suggest"
            }, keysPath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => StartupSettings.Load(builder.Configuration, builder.Environment));

            Assert.Contains("https", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretKey, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithEnabledFeatureAndHttpBaseUrlInDevelopment_Passes()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                ["AddressSuggestions:Enabled"] = "true",
                ["AddressSuggestions:ApiKey"] = SecretKey,
                ["AddressSuggestions:BaseUrl"] = "http://localhost:9999/suggest"
            }, keysPath);

            var settings = StartupSettings.Load(builder.Configuration, builder.Environment);

            Assert.True(settings.AddressSuggestions.Enabled);
            Assert.True(settings.AddressSuggestions.IsConfigured);
            Assert.Equal(
                "http://localhost:9999/suggest",
                settings.AddressSuggestions.BaseUrl);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Theory]
    [InlineData("AddressSuggestions:TimeoutSeconds", "0")]
    [InlineData("AddressSuggestions:TimeoutSeconds", "16")]
    [InlineData("AddressSuggestions:MinQueryLength", "1")]
    [InlineData("AddressSuggestions:MinQueryLength", "11")]
    [InlineData("AddressSuggestions:MinCityQueryLength", "0")]
    [InlineData("AddressSuggestions:MinCityQueryLength", "11")]
    [InlineData("AddressSuggestions:MaxSuggestions", "0")]
    [InlineData("AddressSuggestions:MaxSuggestions", "11")]
    [InlineData("AddressSuggestions:CacheTtlSeconds", "-1")]
    [InlineData("AddressSuggestions:CacheTtlSeconds", "86401")]
    [InlineData("AddressSuggestions:MaxSuggestions", "many")]
    public void Load_WithInvalidAddressSuggestionsValue_Throws(string key, string value)
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

    [Fact]
    public void Load_WithDisabledFeatureAndRelativeBaseUrl_DoesNotThrow()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                ["AddressSuggestions:Enabled"] = "false",
                ["AddressSuggestions:BaseUrl"] = "not a uri"
            }, keysPath);

            var settings = StartupSettings.Load(builder.Configuration, builder.Environment);

            Assert.False(settings.AddressSuggestions.Enabled);
            Assert.False(settings.AddressSuggestions.IsConfigured);
            Assert.Equal("not a uri", settings.AddressSuggestions.BaseUrl);
        }
        finally
        {
            DeleteKeysPath(keysPath);
        }
    }

    [Fact]
    public void Load_WithEnabledFeatureAndRelativeBaseUrl_ThrowsNamingBaseUrl()
    {
        var keysPath = CreateKeysPath();

        try
        {
            var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>
            {
                ["AddressSuggestions:Enabled"] = "true",
                ["AddressSuggestions:ApiKey"] = SecretKey,
                ["AddressSuggestions:BaseUrl"] = "not a uri"
            }, keysPath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => StartupSettings.Load(builder.Configuration, builder.Environment));

            Assert.Contains(
                "AddressSuggestions:BaseUrl",
                exception.Message,
                StringComparison.Ordinal);
            // В сообщении нет ни значения BaseUrl, ни ключа: конфигурация не печатается.
            Assert.DoesNotContain("not a uri", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretKey, exception.Message, StringComparison.Ordinal);
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
            ["DataProtection:KeysPath"] = keysPath,
            // Host filtering требуется только в Production; Development значение не читает.
            ["AllowedHosts"] = "api.barbersearch.test"
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
            "barbersearch-address-suggestions-startup-settings-tests",
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
