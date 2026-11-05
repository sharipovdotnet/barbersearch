using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет разбор секции <c>Logging:File</c>: путь разрешается от content root, каталог
/// создаётся и проверяется на запись, противоречивые параметры ротации отклоняются на старте.
/// </summary>
/// <remarks>
/// Serilog по умолчанию молча игнорирует недоступный файл, поэтому единственная точка, где
/// ошибка пути может уронить старт, — <see cref="FileLoggingSettings.FromConfiguration"/>.
/// Тесты используют временный content root: каталог журналов в дереве репозитория не создаётся.
/// </remarks>
public class FileLoggingSettingsTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), $"bs-logs-{Guid.NewGuid():N}");

    [Fact]
    public void Defaults_ResolvePathAndCreateDirectory()
    {
        var settings = FileLoggingSettings.FromConfiguration(
            BuildConfiguration(),
            CreateEnvironment());

        Assert.True(settings.Enabled);
        Assert.Equal(
            Path.Combine(_contentRoot, "logs", "barbersearch-.log"),
            settings.Path);
        Assert.Equal(RollingInterval.Day, settings.RollingInterval);
        Assert.Equal(14, settings.RetainedFileCountLimit);
        Assert.Equal(50, settings.FileSizeLimitMegabytes);
        Assert.Equal(50L * 1024 * 1024, settings.GetFileSizeLimitBytes());
        Assert.True(Directory.Exists(Path.Combine(_contentRoot, "logs")));
    }

    [Fact]
    public void Disabled_DoesNotTouchFileSystem()
    {
        var settings = FileLoggingSettings.FromConfiguration(
            BuildConfiguration(("Logging:File:Enabled", "false")),
            CreateEnvironment());

        Assert.False(settings.Enabled);
        Assert.False(Directory.Exists(Path.Combine(_contentRoot, "logs")));
    }

    [Fact]
    public void AbsolutePath_IsKeptAsIs()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), $"bs-absolute-{Guid.NewGuid():N}", "api.log");

        var settings = FileLoggingSettings.FromConfiguration(
            BuildConfiguration(("Logging:File:Path", absolutePath)),
            CreateEnvironment());

        Assert.Equal(absolutePath, settings.Path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(absolutePath)));
    }

    [Fact]
    public void InfiniteRolling_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FileLoggingSettings.FromConfiguration(
                BuildConfiguration(("Logging:File:RollingInterval", "Infinite")),
                CreateEnvironment()));

        Assert.Contains("RollingInterval=Infinite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownRollingInterval_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FileLoggingSettings.FromConfiguration(
                BuildConfiguration(("Logging:File:RollingInterval", "Weekly")),
                CreateEnvironment()));

        Assert.Contains("RollingInterval", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPath_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FileLoggingSettings.FromConfiguration(
                BuildConfiguration(("Logging:File:Path", "  ")),
                CreateEnvironment()));

        Assert.Contains("Logging:File:Path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainedFileCountOutOfRange_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FileLoggingSettings.FromConfiguration(
                BuildConfiguration(("Logging:File:RetainedFileCountLimit", "0")),
                CreateEnvironment()));

        Assert.Contains("RetainedFileCountLimit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileSizeLimitOutOfRange_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FileLoggingSettings.FromConfiguration(
                BuildConfiguration(("Logging:File:FileSizeLimitMegabytes", "0")),
                CreateEnvironment()));

        Assert.Contains("FileSizeLimitMegabytes", exception.Message, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private TestWebHostEnvironment CreateEnvironment() => new() { ContentRootPath = _contentRoot };

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
    {
        var settings = new Dictionary<string, string?>();

        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    /// <summary>
    /// Минимальное окружение хоста: настройки проверяют только content root.
    /// </summary>
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "BarberSearch.Web";

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
