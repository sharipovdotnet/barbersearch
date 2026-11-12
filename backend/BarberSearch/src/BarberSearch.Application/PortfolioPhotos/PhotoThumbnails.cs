using BarberSearch.Domain;

namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Производный адрес миниатюры для ответа API: <c>{url}?size=md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Поле <c>thumbnailUrl</c> контракта заполняется <b>вычисляемым</b> адресом, а не строкой из
/// БД: миниатюра — это вариант того же файла, поэтому отдельная колонка не нужна и миграции
/// в срезе нет. В <c>portfolio_photos.thumbnail_url</c> остаётся то, что записала загрузка
/// (сегодня — <c>null</c>), и это значение по-прежнему главнее вычислимого: так легаси-строки,
/// добавленные старым JSON-путём, не теряют свою ссылку.
/// </para>
/// <para>
/// Для абсолютных внешних URL (легаси-фотографии, которые сервер не обслуживает) миниатюры
/// нет: такой файл не раздаётся маршрутом <c>/api/media/portfolio/…</c>, и <c>?size=md</c>
/// превратился бы в битую ссылку.
/// </para>
/// </remarks>
public static class PhotoThumbnails
{
    /// <summary>
    /// Query-параметр размера у media-маршрутов: только <c>size</c>, только три значения.
    /// </summary>
    public const string SIZE_QUERY_PARAMETER = "size";

    /// <summary>
    /// Значение среднего варианта: ширина 512 px для фотографии портфолио.
    /// </summary>
    public const string MEDIUM_SIZE_QUERY = "?size=md";

    /// <summary>
    /// Адрес миниатюры для фотографии портфолио.
    /// </summary>
    /// <param name="url">Собственный media-URL кадра либо внешняя ссылка.</param>
    /// <param name="storedThumbnailUrl">Значение из БД: легаси-миниатюра, если она была задана.</param>
    /// <returns>
    /// Сохранённое значение, если оно есть; <c>{url}?size=md</c> для собственного media-URL;
    /// иначе <c>null</c>.
    /// </returns>
    public static string? Of(string? url, string? storedThumbnailUrl)
    {
        if (storedThumbnailUrl is not null)
        {
            return storedThumbnailUrl;
        }

        if (url is null)
        {
            return null;
        }

        var prefix = Constants.PORTFOLIO_MEDIA_PATH_PREFIX + "/";

        return url.StartsWith(prefix, StringComparison.Ordinal)
            ? url + MEDIUM_SIZE_QUERY
            : null;
    }

    /// <summary>
    /// Адрес миниатюры для уже материализованной строки ответа.
    /// </summary>
    /// <remarks>
    /// Отдельная перегрузка нужна, чтобы применять правило после материализации: SQL-проекция
    /// не умеет конкатенацию с проверкой префикса, а ломать её ради одного поля нельзя.
    /// </remarks>
    /// <param name="photo">Строка ответа, прочитанная из БД.</param>
    /// <returns>Копия строки с заполненным <c>thumbnailUrl</c>.</returns>
    public static Contracts.BarberProfiles.GetBarberProfile.BarberProfilePhotoResponse Of(
        Contracts.BarberProfiles.GetBarberProfile.BarberProfilePhotoResponse photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        return photo with { ThumbnailUrl = Of(photo.Url, photo.ThumbnailUrl) };
    }
}
