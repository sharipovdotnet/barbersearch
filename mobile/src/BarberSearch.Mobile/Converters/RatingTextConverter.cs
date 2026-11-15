using System.Globalization;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Converters;

/// <summary>
/// Рейтинг строки поиска: «Рейтинг: 4,8» на языке интерфейса.
/// </summary>
/// <remarks>
/// <c>StringFormat</c> в XAML не умеет переводить подпись, а рейтинг приходит числом из
/// контракта. Конвертер — самое узкое место, где можно собрать строку через <see cref="BarberText"/>.
/// </remarks>
public sealed class RatingTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var rating = value is decimal number ? number : (decimal?)null;

        return Loc.T("Рейтинг: {0}", BarberText.Rating(rating));
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
