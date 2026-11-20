using System.Globalization;

namespace BarberSearch.Mobile.Converters;

/// <summary>
/// Показывает картинку из байтов, уже прочитанных приложением.
/// </summary>
/// <remarks>
/// <para>
/// Превью модерации закрыты ролью: <c>api/media/*</c> отдаёт неодобренный кадр только
/// администратору, а загрузчик картинок платформы ходит без заголовков и получает <c>404</c>.
/// Поэтому байты приносит <c>IAdminMediaApi</c>, а картинка собирается здесь — из потока в
/// памяти, без сети.
/// </para>
/// <para>
/// Пока байтов нет, конвертер возвращает <c>null</c>: <c>Image</c> не рисует ничего, и на
/// карточке остаётся подложка. Это и есть состояние загрузки, отдельного индикатора не нужно.
/// </para>
/// </remarks>
public sealed class ImageBytesConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte[] { Length: > 0 } bytes
            ? ImageSource.FromStream(() => new MemoryStream(bytes, writable: false))
            : null;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
