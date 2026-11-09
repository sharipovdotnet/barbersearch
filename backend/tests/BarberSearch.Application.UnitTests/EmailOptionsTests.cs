using BarberSearch.Application.EmailVerifications;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Настройки почты: без валидного адреса отправителя, хоста или порта отправка не считается
/// настроенной, а пустая секция остаётся нормальным состоянием.
/// </summary>
public class EmailOptionsTests
{
    [Fact]
    public void IsConfigured_WithValidSettings_IsTrue()
    {
        var options = new EmailOptions
        {
            Enabled = true,
            Host = "smtp.example.com",
            Port = 587,
            FromAddress = "no-reply@barbersearch.test"
        };

        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Defaults_AreDisabledWithEmptyCredentialsAndFiveSecondTimeout()
    {
        var options = new EmailOptions();

        Assert.False(options.Enabled);
        Assert.False(options.IsConfigured);
        Assert.Equal(587, options.Port);
        Assert.True(options.UseTls);
        Assert.Equal("BarberSearch", options.FromName);
        Assert.Equal(5, options.TimeoutSeconds);
        Assert.Null(options.UserName);
        Assert.Null(options.Password);
    }

    [Theory]
    [InlineData(false, "localhost", 1025, "no-reply@barbersearch.test")]
    [InlineData(true, "", 1025, "no-reply@barbersearch.test")]
    [InlineData(true, "   ", 1025, "no-reply@barbersearch.test")]
    [InlineData(true, "localhost", 0, "no-reply@barbersearch.test")]
    [InlineData(true, "localhost", -1, "no-reply@barbersearch.test")]
    [InlineData(true, "localhost", 1025, "")]
    [InlineData(true, "localhost", 1025, "not-an-address")]
    public void IsConfigured_WithIncompleteSettings_IsFalse(
        bool enabled,
        string host,
        int port,
        string fromAddress)
    {
        var options = new EmailOptions
        {
            Enabled = enabled,
            Host = host,
            Port = port,
            FromAddress = fromAddress
        };

        Assert.False(options.IsConfigured);
    }
}
