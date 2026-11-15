using System.Globalization;

namespace BarberSearch.Mobile.Converters;

/// <summary>
/// Показывает элемент только при непустом значении: расстояние в выдаче есть не всегда.
/// </summary>
/// <remarks>
/// <c>IsVisible</c> не умеет принимать <c>double?</c> из контракта, а пустая подпись
/// всё равно занимала бы строку в карточке. Конвертер — самое узкое место для этой проверки.
/// </remarks>
public sealed class NotNullConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
