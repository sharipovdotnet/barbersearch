using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки разбора <c>push.json</c>: отсутствие и любая неполнота — «канал выключен».
/// </summary>
/// <remarks>
/// Именно это правило держит обещание «без конфигурации приложение ведёт себя как раньше»:
/// ни разрешений, ни переключателя, ни исключения на старте.
/// </remarks>
public sealed class PushOptionsTests
{
    private const string ValidJson = """
        {
          "enabled": true,
          "projectId": "barbersearch-test",
          "applicationId": "1:000000000000:android:0000000000000000000000",
          "apiKey": "test-api-key",
          "senderId": "000000000000"
        }
        """;

    [Fact]
    public void Parse_MissingJson_ReturnsDisabled()
    {
        Assert.Same(PushOptions.Disabled, PushOptions.Parse(null));
        Assert.Same(PushOptions.Disabled, PushOptions.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsDisabled()
    {
        Assert.Same(PushOptions.Disabled, PushOptions.Parse("{ not json"));
    }

    [Fact]
    public void Parse_DisabledFlag_ReturnsDisabled()
    {
        const string json = """
            {
              "enabled": false,
              "projectId": "barbersearch-test",
              "applicationId": "app-id",
              "apiKey": "key",
              "senderId": "sender"
            }
            """;

        Assert.Same(PushOptions.Disabled, PushOptions.Parse(json));
    }

    [Fact]
    public void Parse_MissingField_ReturnsDisabled()
    {
        const string json = """
            {
              "enabled": true,
              "projectId": "barbersearch-test",
              "applicationId": "app-id",
              "apiKey": "",
              "senderId": "sender"
            }
            """;

        Assert.Same(PushOptions.Disabled, PushOptions.Parse(json));
    }

    [Fact]
    public void Parse_ValidJson_ReturnsValues()
    {
        var options = PushOptions.Parse(ValidJson);

        Assert.True(options.Enabled);
        Assert.Equal("barbersearch-test", options.ProjectId);
        Assert.Equal("1:000000000000:android:0000000000000000000000", options.ApplicationId);
        Assert.Equal("test-api-key", options.ApiKey);
        Assert.Equal("000000000000", options.SenderId);
    }
}
