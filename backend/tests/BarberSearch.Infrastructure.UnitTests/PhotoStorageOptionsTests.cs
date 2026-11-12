using BarberSearch.Web.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет разбор секции <c>Storage</c>: провайдер выбирается строкой, локальный путь
/// разрешается и проверяется, S3 требует обязательные ключи и не трогает диск.
/// </summary>
/// <remarks>
/// Настройки не ходят в сеть: существование бакета проверяется на старте хоста
/// (<c>S3StorageInitializer</c>), поэтому здесь проверяются только исключения конфигурации.
/// Content root каждый раз временный: <c>App_Data</c> в дереве репозитория не появляется.
/// </remarks>
public class PhotoStorageOptionsTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), $"bs-storage-{Guid.NewGuid():N}");

    [Fact]
    public void Defaults_UseLocalProviderAndCreateDirectory()
    {
        var options = PhotoStorageOptions.FromConfiguration(
            BuildConfiguration(),
            CreateEnvironment());

        Assert.Equal(PhotoStorageProvider.Local, options.Provider);
        Assert.Equal(Path.Combine(_contentRoot, "App_Data", "photos"), options.RootPath);
        Assert.True(Directory.Exists(options.RootPath));

        // Секция S3 без значений: пустые поля и дефолты, но она не используется.
        Assert.Equal(string.Empty, options.S3.Bucket);
        Assert.True(options.S3.ForcePathStyle);
        Assert.False(options.S3.EnsureBucket);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("LOCAL")]
    [InlineData(" Local ")]
    public void Provider_IsParsedCaseInsensitively(string value)
    {
        var options = PhotoStorageOptions.FromConfiguration(
            BuildConfiguration(("Storage:Provider", value)),
            CreateEnvironment());

        Assert.Equal(PhotoStorageProvider.Local, options.Provider);
    }

    [Fact]
    public void Provider_S3_IsParsedWithSurroundingSpaces()
    {
        var options = PhotoStorageOptions.FromConfiguration(
            BuildS3Configuration(" s3 "),
            CreateEnvironment());

        Assert.Equal(PhotoStorageProvider.S3, options.Provider);
        Assert.Equal("barbersearch", options.S3.Bucket);
        Assert.Equal("http://localhost:9000", options.S3.ServiceUrl);
        Assert.Equal("us-east-1", options.S3.Region);
        Assert.True(options.S3.ForcePathStyle);
        Assert.False(options.S3.EnsureBucket);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("minio")]
    public void Provider_NumericOrUnknown_IsRejected(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PhotoStorageOptions.FromConfiguration(
                BuildConfiguration(("Storage:Provider", value)),
                CreateEnvironment()));

        Assert.Contains("Allowed values", exception.Message, StringComparison.Ordinal);
        Assert.Contains(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void S3_MissingRequiredKeys_IsRejectedWithoutSecretValues()
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", "S3"),
            ("Storage:S3:AccessKey", "top-secret-access"),
            ("Storage:S3:SecretKey", "top-secret-secret"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PhotoStorageOptions.FromConfiguration(configuration, CreateEnvironment()));

        Assert.Contains("Storage:S3:Bucket", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Storage:S3:ServiceUrl", exception.Message, StringComparison.Ordinal);

        // Секреты в текст исключения не попадают — только имена ключей конфигурации.
        Assert.DoesNotContain("top-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void S3_InvalidServiceUrl_IsRejectedWithoutPrintingIt()
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", "S3"),
            ("Storage:S3:Bucket", "barbersearch"),
            ("Storage:S3:ServiceUrl", "s3.internal:9000"),
            ("Storage:S3:AccessKey", "barbersearch"),
            ("Storage:S3:SecretKey", "barbersearch-e2e"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PhotoStorageOptions.FromConfiguration(configuration, CreateEnvironment()));

        Assert.Contains("Storage:S3:ServiceUrl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3.internal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void S3_DoesNotRequireRootPathAndDoesNotCreateDirectory()
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", "S3"),
            ("Storage:RootPath", "photo-store"),
            ("Storage:S3:Bucket", "barbersearch"),
            ("Storage:S3:ServiceUrl", "http://localhost:9000"),
            ("Storage:S3:AccessKey", "barbersearch"),
            ("Storage:S3:SecretKey", "barbersearch-e2e"));

        var options = PhotoStorageOptions.FromConfiguration(configuration, CreateEnvironment());

        Assert.Equal(PhotoStorageProvider.S3, options.Provider);
        Assert.Equal(PhotoStorageOptions.DEFAULT_ROOT_PATH, options.RootPath);
        Assert.False(Directory.Exists(Path.Combine(_contentRoot, "photo-store")));
        Assert.False(Directory.Exists(Path.Combine(_contentRoot, "App_Data")));
    }

    [Fact]
    public void Local_EmptyRootPath_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PhotoStorageOptions.FromConfiguration(
                BuildConfiguration(("Storage:RootPath", "  ")),
                CreateEnvironment()));

        Assert.Contains("Storage:RootPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_UnwritablePath_IsRejected()
    {
        Directory.CreateDirectory(_contentRoot);

        var filePath = Path.Combine(_contentRoot, "photo-file");
        File.WriteAllText(filePath, "not a directory");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PhotoStorageOptions.FromConfiguration(
                BuildConfiguration(("Storage:RootPath", filePath)),
                CreateEnvironment()));

        Assert.Contains("not writable", exception.Message, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог — не предмет проверки; ОС уберёт его сама.
        }

        GC.SuppressFinalize(this);
    }

    private TestWebHostEnvironment CreateEnvironment() => new() { ContentRootPath = _contentRoot };

    private static IConfiguration BuildS3Configuration(string provider)
        => BuildConfiguration(
            ("Storage:Provider", provider),
            ("Storage:S3:Bucket", "barbersearch"),
            ("Storage:S3:ServiceUrl", "http://localhost:9000"),
            ("Storage:S3:AccessKey", "barbersearch"),
            ("Storage:S3:SecretKey", "barbersearch-e2e"));

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
    /// Минимальное окружение хоста: настройкам нужен только content root.
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
