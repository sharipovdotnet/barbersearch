using System.Security.Cryptography;
using System.Text.Json;
using BarberSearch.Application.Push;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Настройки push-канала: «настроен» только при наличии project id и разбираемого ключа.
/// </summary>
/// <remarks>
/// Ключ читается лениво из <c>Push:ServiceAccountJson</c> или из файла по
/// <c>Push:ServiceAccountPath</c>. Битый ключ — выключенный канал, а не исключение:
/// приложение обязано работать без credentials.
/// </remarks>
public class PushOptionsTests
{
    [Fact]
    public void Defaults_AreDisabledWithoutCredentials()
    {
        var options = new PushOptions();

        Assert.False(options.Enabled);
        Assert.False(options.IsConfigured);
        Assert.False(options.DevMode);
        Assert.Null(options.ServiceAccount);
        Assert.Equal(60, options.IntervalSeconds);
        Assert.Equal(20, options.BatchSize);
        Assert.Equal(60, options.ReminderLeadMinutes);
        Assert.Equal(5, options.MaxAttempts);
        Assert.Equal(30, options.RetentionDays);
        Assert.Equal(180, options.DeviceTokenRetentionDays);
        Assert.Equal(5, options.MaxTokensPerUser);
    }

    [Fact]
    public void IsConfigured_WithProjectIdAndAValidKey_IsTrue()
    {
        var options = new PushOptions
        {
            ProjectId = "demo-project",
            ServiceAccountJson = CreateServiceAccountJson()
        };

        Assert.True(options.IsConfigured);
        Assert.NotNull(options.ServiceAccount);
        Assert.Equal("test@example.iam.gserviceaccount.com", options.ServiceAccount.ClientEmail);
    }

    [Fact]
    public void IsConfigured_WithABrokenKey_IsFalse()
    {
        var options = new PushOptions
        {
            ProjectId = "demo-project",
            ServiceAccountJson = "{ not a service account }"
        };

        Assert.False(options.IsConfigured);
        Assert.Null(options.ServiceAccount);
    }

    [Fact]
    public void IsConfigured_WithoutProjectId_IsFalse()
    {
        var options = new PushOptions
        {
            ProjectId = "  ",
            ServiceAccountJson = CreateServiceAccountJson()
        };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void ToString_DoesNotLeakTheKeyOrTheServiceAccount()
    {
        var options = new PushOptions
        {
            Enabled = true,
            ProjectId = "demo-project",
            ServiceAccountJson = CreateServiceAccountJson()
        };

        var text = options.ToString();

        Assert.DoesNotContain("PRIVATE KEY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test@example", text, StringComparison.Ordinal);
        Assert.DoesNotContain("demo-project", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WithoutClientEmailOrPrivateKey_ReturnsFalse()
    {
        var withEmailOnly = """{"type":"service_account","client_email":"a@b.c"}""";
        var withWrongType = """{"type":"authorized_user","client_email":"a@b.c","private_key":"-----BEGIN PRIVATE KEY-----"}""";

        Assert.False(ServiceAccountKey.TryParse(withEmailOnly, out _));
        Assert.False(ServiceAccountKey.TryParse(withWrongType, out _));
    }

    private static string CreateServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);

        return JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = "demo-project",
            client_email = "test@example.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = ServiceAccountKey.DEFAULT_TOKEN_URI
        });
    }
}
