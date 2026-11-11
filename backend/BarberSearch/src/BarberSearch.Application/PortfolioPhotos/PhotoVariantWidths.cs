namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Ширины вариантов, имена производных файлов и ETag для одного media-идентификатора.
/// </summary>
/// <remarks>
/// <para>
/// Единственное место, где записаны числа <c>256/512/1024</c> (портфолио) и <c>96/192/384</c>
/// (аватар). Те же числа продублированы в <c>frontend/src/lib/media.ts</c> как дескрипторы
/// <c>srcset</c>: это <b>часть контракта</b>, и менять их можно только вместе с фронтендом
/// (backend/AGENT.md §16.1, frontend/AGENT.md §12.2).
/// </para>
/// <para>
/// Имя производного файла — <c>{mediaId:N}-{sm|md|lg}.webp</c> в том же каталоге, что исходник.
/// Расширение всегда <c>webp</c>: другие форматы вариантов не производятся.
/// </para>
/// <para>
/// ETag строится по идентификатору медиа, а не по содержимому: байты для пары
/// <c>(mediaId, вариант)</c> неизменяемы, потому что замена кадра — это новый <c>mediaId</c>.
/// Поэтому хешировать файл (читать его целиком) для честного кэша не нужно.
/// </para>
/// </remarks>
public static class PhotoVariantWidths
{
    /// <summary>
    /// Ширина маленького варианта фотографии портфолио: <c>?size=sm</c>.
    /// </summary>
    public const int PORTFOLIO_SMALL = 256;

    /// <summary>
    /// Ширина среднего варианта фотографии портфолио: <c>?size=md</c>.
    /// </summary>
    public const int PORTFOLIO_MEDIUM = 512;

    /// <summary>
    /// Ширина большого варианта фотографии портфолио: <c>?size=lg</c>.
    /// </summary>
    public const int PORTFOLIO_LARGE = 1024;

    /// <summary>
    /// Ширина маленького варианта аватара: <c>?size=sm</c>.
    /// </summary>
    public const int AVATAR_SMALL = 96;

    /// <summary>
    /// Ширина среднего варианта аватара: <c>?size=md</c>.
    /// </summary>
    public const int AVATAR_MEDIUM = 192;

    /// <summary>
    /// Ширина большого варианта аватара: <c>?size=lg</c>.
    /// </summary>
    public const int AVATAR_LARGE = 384;

    /// <summary>
    /// Расширение производного файла: вариант всегда WebP.
    /// </summary>
    public const string VARIANT_EXTENSION = "webp";

    /// <summary>
    /// Целевая ширина варианта в пикселях.
    /// </summary>
    /// <param name="variant">Вариант, отличный от <see cref="PhotoVariant.Original"/>.</param>
    /// <param name="isAvatar"><c>true</c> — аватар, <c>false</c> — фотография портфолио.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Вариант вне объявленного перечисления: пользовательских размеров нет.
    /// </exception>
    public static int Width(PhotoVariant variant, bool isAvatar) => (variant, isAvatar) switch
    {
        (PhotoVariant.Small, false) => PORTFOLIO_SMALL,
        (PhotoVariant.Medium, false) => PORTFOLIO_MEDIUM,
        (PhotoVariant.Large, false) => PORTFOLIO_LARGE,
        (PhotoVariant.Small, true) => AVATAR_SMALL,
        (PhotoVariant.Medium, true) => AVATAR_MEDIUM,
        (PhotoVariant.Large, true) => AVATAR_LARGE,
        _ => throw new ArgumentOutOfRangeException(
            nameof(variant),
            variant,
            "A derivative variant is expected: the original is not resized.")
    };

    /// <summary>
    /// Суффикс варианта в query и в имени файла: <c>sm</c>, <c>md</c> или <c>lg</c>.
    /// </summary>
    /// <param name="variant">Вариант, отличный от <see cref="PhotoVariant.Original"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Вариант вне объявленного перечисления.</exception>
    public static string Suffix(PhotoVariant variant) => variant switch
    {
        PhotoVariant.Small => "sm",
        PhotoVariant.Medium => "md",
        PhotoVariant.Large => "lg",
        _ => throw new ArgumentOutOfRangeException(
            nameof(variant),
            variant,
            "A derivative variant is expected: the original has no suffix.")
    };

    /// <summary>
    /// Имя производного файла: <c>{mediaId:N}-{sm|md|lg}.webp</c>.
    /// </summary>
    /// <param name="mediaId">Идентификатор медиафайла.</param>
    /// <param name="variant">Вариант, отличный от <see cref="PhotoVariant.Original"/>.</param>
    public static string FileName(Guid mediaId, PhotoVariant variant)
        => $"{mediaId:N}-{Suffix(variant)}.{VARIANT_EXTENSION}";

    /// <summary>
    /// Маска всех производных файлов одного кадра: <c>{mediaId:N}-*.webp</c>.
    /// </summary>
    /// <remarks>
    /// Используется только при удалении: перечисление идёт по маске в каталоге исходника,
    /// а не по содержимому query. Вариант приходит типом, поэтому <c>..</c> в пути
    /// принципиально невозможен.
    /// </remarks>
    /// <param name="mediaId">Идентификатор медиафайла.</param>
    public static string FilePattern(Guid mediaId) => $"{mediaId:N}-*.{VARIANT_EXTENSION}";

    /// <summary>
    /// Сильный ETag файла: <c>"&lt;mediaId:N&gt;"</c> для исходника и
    /// <c>"&lt;mediaId:N&gt;-sm"</c> для варианта.
    /// </summary>
    /// <remarks>
    /// Кавычки входят в строку: заголовок <c>ETag</c> и сравнение с <c>If-None-Match</c>
    /// работают с одним и тем же значением, поэтому слабые ETag (<c>W/</c>) не порождаются
    /// и не принимаются.
    /// </remarks>
    /// <param name="mediaId">Идентификатор медиафайла.</param>
    /// <param name="variant">Вариант: исходник или один из трёх производных.</param>
    public static string EntityTag(Guid mediaId, PhotoVariant variant)
        => variant == PhotoVariant.Original
            ? $"\"{mediaId:N}\""
            : $"\"{mediaId:N}-{Suffix(variant)}\"";
}
