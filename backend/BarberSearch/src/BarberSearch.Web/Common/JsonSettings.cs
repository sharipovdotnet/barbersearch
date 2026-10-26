using System.Text.Json;
using System.Text.Json.Serialization;

namespace BarberSearch.Web.Common;

/// <summary>
/// Настройки JSON для MVC.
/// </summary>
/// <remarks>
/// Перечисления читаются и пишутся строками. <see cref="JsonStringEnumConverter"/> без аргументов
/// по умолчанию принимает и целые числа (<c>AllowIntegerValues = true</c>), поэтому запрос
/// <c>"paymentMethods": [999]</c> доходил бы до домена и сохранялся в массив БД. Здесь целые числа
/// запрещены: неизвестное числовое значение отклоняется с <see cref="JsonException"/> ещё на этапе
/// десериализации, до вызова обработчика.
/// </remarks>
public static class JsonSettings
{
    /// <summary>
    /// Применяет настройки к параметрам JSON MVC.
    /// </summary>
    /// <param name="options">Параметры из <c>AddJsonOptions</c>.</param>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    }
}
