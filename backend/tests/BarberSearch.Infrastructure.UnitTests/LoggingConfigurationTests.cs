using System.Text.Json;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет уровни логирования в настройках веб-слоя (R-13): в боевом окружении EF Core
/// не должен писать SQL-команды с параметрами, а в Development подробный лог сохраняется.
/// </summary>
/// <remarks>
/// <c>appsettings.json</c> и <c>appsettings.Development.json</c> подключены к тестовому проекту
/// content-ссылками на файлы <c>BarberSearch.Web</c>, поэтому проверяются те же настройки,
/// которые уходят в публикацию, и тест не зависит от рабочего каталога тестового хоста.
/// </remarks>
public class LoggingConfigurationTests
{
    [Fact]
    public void ProductionLogging_SetsEfCoreCategoriesToWarning()
    {
        var levels = ReadLogLevels("appsettings.json");

        Assert.Equal("Warning", GetLevel(levels, "Microsoft.EntityFrameworkCore"));
        Assert.Equal("Warning", GetLevel(levels, "Microsoft.EntityFrameworkCore.Database.Command"));

        // Категории, не относящиеся к R-13, не ухудшаются: собственные Information-логи
        // приложения остаются, шум ASP.NET Core — предупреждением.
        Assert.Equal("Information", GetLevel(levels, "Default"));
        Assert.Equal("Warning", GetLevel(levels, "Microsoft.AspNetCore"));
    }

    [Fact]
    public void DevelopmentLogging_KeepsEfCoreAtInformation()
    {
        var levels = ReadLogLevels("appsettings.Development.json");

        // В Development SQL-команды нужны для отладки: боевое ограничение снимается явно,
        // а не наследованием от Default.
        Assert.Equal("Information", GetLevel(levels, "Microsoft.EntityFrameworkCore"));
        Assert.Equal("Information", GetLevel(levels, "Microsoft.EntityFrameworkCore.Database.Command"));
        Assert.Equal("Information", GetLevel(levels, "Default"));
    }

    /// <summary>
    /// Читает секцию <c>Logging:LogLevel</c> из файла настроек рядом с тестовой сборкой.
    /// </summary>
    /// <param name="fileName">Имя файла настроек, скопированного в выходной каталог.</param>
    /// <returns>Элемент <c>LogLevel</c>, не зависящий от жизни <see cref="JsonDocument"/>.</returns>
    private static JsonElement ReadLogLevels(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement
            .GetProperty("Logging")
            .GetProperty("LogLevel")
            .Clone();
    }

    /// <summary>
    /// Возвращает уровень категории или <see langword="null"/>, если категория не настроена.
    /// </summary>
    /// <param name="levels">Элемент <c>Logging:LogLevel</c>.</param>
    /// <param name="category">Имя категории логирования.</param>
    /// <returns>Строковое значение уровня либо <see langword="null"/>.</returns>
    private static string? GetLevel(JsonElement levels, string category) =>
        levels.TryGetProperty(category, out var level) ? level.GetString() : null;
}
