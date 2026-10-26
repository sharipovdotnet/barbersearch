using BarberSearch.Web.Auth;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет fail-closed валидацию <c>AllowedHosts</c> в <see cref="StartupSettings"/> (R-47).
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
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            "barbersearch-startup-settings-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(keysPath);

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] =
                    "Host=127.0.0.1;Port=59999;Database=missing;Username=postgres;Password=postgres;Timeout=1",
                ["Auth:SigningKey"] = new string('s', AuthOptions.MIN_SIGNING_KEY_LENGTH),
                ["DataProtection:KeysPath"] = keysPath,
                ["AllowedHosts"] = "*"
            });

            var exception = Assert.Throws<InvalidOperationException>(
                () => StartupSettings.Load(builder.Configuration, builder.Environment));

            Assert.Contains("AllowedHosts", exception.Message, StringComparison.Ordinal);
            Assert.Contains("'*' is not allowed", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(keysPath))
            {
                Directory.Delete(keysPath, recursive: true);
            }
        }
    }
}
