using System.Text.Json;
using System.Text.Json.Serialization;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Настройки JSON, общие для всех запросов и ответов API.
/// </summary>
/// <remarks>
/// Совпадают с сервером: ASP.NET Core пишет camelCase, перечисления — строками
/// (<c>JsonSettings.Configure</c> в Web). Целые значения перечислений запрещены так же, как
/// на сервере: неизвестное число должно падать на разборе, а не уезжать в модель.
/// </remarks>
public static class ApiJson
{
    /// <summary>
    /// Единственный экземпляр настроек: <see cref="JsonSerializerOptions"/> нельзя менять после
    /// первого использования, а создавать его на каждый запрос — лишняя работа.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));

        return options;
    }
}
