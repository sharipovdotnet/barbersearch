using BarberSearch.Application.PortfolioPhotos;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Раскладка файлов в хранилище: единственное место, где известны префиксы и имена ключей.
/// </summary>
/// <remarks>
/// <para>
/// Ключ объекта S3 повторяет путь локального провайдера, только с прямыми слэшами:
/// <c>portfolio/{ownerId:N}/{mediaId:N}.{ext}</c> и <c>avatars/{ownerId:N}/{mediaId:N}.{ext}</c>,
/// варианты — <c>{mediaId:N}-{sm|md|lg}.webp</c> в том же «каталоге». Поэтому смена провайдера
/// не меняет публичные URL, а перенос уже загруженных файлов сводится к копированию каталога
/// в бакет (операторская операция, в коде не автоматизируется).
/// </para>
/// <para>
/// Ключ строится только из <see cref="Guid"/>-идентификаторов, разобранных из media-URL, и
/// расширения из белого списка: строка запроса и <c>..</c> в ключ не попадают.
/// </para>
/// </remarks>
internal static class PhotoMediaKeys
{
    /// <summary>
    /// «Каталог» файлов портфолио: первый сегмент ключа и подкаталог локального корня.
    /// </summary>
    public const string PORTFOLIO_DIRECTORY = "portfolio";

    /// <summary>
    /// «Каталог» файлов аватаров: входит в тот же бэкап, что и <c>portfolio/</c>.
    /// </summary>
    public const string AVATAR_DIRECTORY = "avatars";

    /// <summary>
    /// Префикс вида файлов: <c>portfolio</c> для фотографии, <c>avatars</c> для аватара.
    /// </summary>
    /// <param name="isAvatar"><c>true</c> — аватар, <c>false</c> — фотография портфолио.</param>
    public static string Directory(bool isAvatar)
        => isAvatar ? AVATAR_DIRECTORY : PORTFOLIO_DIRECTORY;

    /// <summary>
    /// Ключ исходника: <c>{directory}/{ownerId:N}/{mediaId:N}.{extension}</c>.
    /// </summary>
    /// <param name="isAvatar"><c>true</c> — аватар, <c>false</c> — фотография портфолио.</param>
    /// <param name="ownerId">Профиль барбера либо пользователь — владелец файла.</param>
    /// <param name="mediaId">Идентификатор медиафайла.</param>
    /// <param name="extension">Расширение без точки: <c>jpg</c>, <c>png</c> или <c>webp</c>.</param>
    public static string Original(bool isAvatar, Guid ownerId, Guid mediaId, string extension)
        => $"{Directory(isAvatar)}/{ownerId:N}/{mediaId:N}.{extension}";

    /// <summary>
    /// Ключ производного варианта: та же «папка», имя из
    /// <see cref="PhotoVariantWidths.FileName"/>.
    /// </summary>
    /// <param name="isAvatar"><c>true</c> — аватар, <c>false</c> — фотография портфолио.</param>
    /// <param name="ownerId">Профиль барбера либо пользователь — владелец файла.</param>
    /// <param name="mediaId">Идентификатор медиафайла.</param>
    /// <param name="variant">Вариант, отличный от <see cref="PhotoVariant.Original"/>.</param>
    public static string Variant(
        bool isAvatar,
        Guid ownerId,
        Guid mediaId,
        PhotoVariant variant)
        => $"{Directory(isAvatar)}/{ownerId:N}/{PhotoVariantWidths.FileName(mediaId, variant)}";
}
