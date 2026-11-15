using System.Globalization;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Converters;

/// <summary>
/// Расстояние строки поиска: «850 м» или «1,2 км» на языке интерфейса.
/// </summary>
/// <remarks>
/// Контракт отдаёт расстояние числом в километрах. Строку собирает <c>BarberText.Distance</c>,
/// чтобы метры, запятая и единицы не расходились с остальными подписями приложения.
/// </remarks>
public sealed class DistanceTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BarberText.Distance(value is double kilometers ? kilometers : null);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
