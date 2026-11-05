using BarberSearch.Web.Deployment;
using Microsoft.Extensions.Configuration;
using Serilog.Events;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет перевод уровней из <c>Logging:LogLevel</c> в уровни Serilog.
/// </summary>
/// <remarks>
/// Serilog получает уровни программно, а не через <c>Serilog:MinimumLevel</c>: имена в
/// <c>Microsoft.Extensions.Logging</c> и <c>Serilog</c> различаются (<c>Trace</c> — <c>Verbose</c>,
/// <c>Critical</c> — <c>Fatal</c>), а <c>None</c> у Serilog отсутствует. Ошибка в этом переводе
/// молча включила или выключила бы категорию, поэтому случаи проверяются явно.
/// </remarks>
public class LoggingLevelsTests
{
    [Fact]
    public void MissingSection_FallsBackToInformation()
    {
        var levels = LoggingLevels.FromConfiguration(BuildConfiguration());

        Assert.Equal(LogEventLevel.Information, levels.Default);
        Assert.Empty(levels.Overrides);
    }

    [Fact]
    public void DefaultAndCategories_AreReadSeparately()
    {
        var levels = LoggingLevels.FromConfiguration(BuildConfiguration(
            ("Logging:LogLevel:Default", "Warning"),
            ("Logging:LogLevel:Microsoft.AspNetCore", "Error"),
            ("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Debug")));

        Assert.Equal(LogEventLevel.Warning, levels.Default);
        Assert.Contains(
            levels.Overrides,
            level => level.Key == "Microsoft.AspNetCore" && level.Value == LogEventLevel.Error);
        Assert.Contains(
            levels.Overrides,
            level => level.Key == "Microsoft.EntityFrameworkCore" && level.Value == LogEventLevel.Debug);
        Assert.DoesNotContain(levels.Overrides, level => level.Key == "Default");
    }

    [Theory]
    [InlineData("Trace", LogEventLevel.Verbose)]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("Information", LogEventLevel.Information)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("Critical", LogEventLevel.Fatal)]
    [InlineData("warning", LogEventLevel.Warning)]
    public void MicrosoftLevelNames_AreMappedToSerilog(string levelName, LogEventLevel expected)
    {
        var levels = LoggingLevels.FromConfiguration(
            BuildConfiguration(("Logging:LogLevel:Default", levelName)));

        Assert.Equal(expected, levels.Default);
    }

    [Fact]
    public void None_IsRejected_WithExplanation()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LoggingLevels.FromConfiguration(BuildConfiguration(("Logging:LogLevel:Default", "None"))));

        Assert.Contains("None", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be disabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownLevel_IsRejected_WithCategoryInMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LoggingLevels.FromConfiguration(
                BuildConfiguration(("Logging:LogLevel:BarberSearch", "Loud"))));

        Assert.Contains("Logging:LogLevel:BarberSearch", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Loud", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
    {
        var settings = new Dictionary<string, string?>();

        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
