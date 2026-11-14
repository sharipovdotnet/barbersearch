namespace BarberSearch.Mobile.Core.Media;

/// <summary>
/// Адреса картинок: склейка с адресом API и варианты размера.
/// </summary>
/// <remarks>
/// <para>
/// Сервер отдаёт <c>avatarUrl</c>, <c>url</c> и <c>thumbnailUrl</c> относительными
/// (<c>/api/media/...</c>) либо абсолютными легаси-ссылками. <c>Image.Source</c> ждёт URI,
/// который можно загрузить, поэтому относительный адрес склеивается с <c>BaseAddress</c>
/// клиента: адрес API знает композиционный корень, а Core — нет.
/// </para>
/// <para>
/// Вариант размера — тот же файл в другом разрешении, отдельной колонки у него нет:
/// сервер отдаёт WebP по параметру <c>size</c>. Поэтому <see cref="Resize"/> повторяет
/// <c>frontend/src/lib/media.ts</c>: query дописывается только своим префиксам раздачи
/// и только если в адресе ещё нет <c>?</c> или <c>#</c>. Чужой URL сервер вариантами
/// не обслуживает — с ним уйдёт исходник, а не 404.
/// </para>
/// </remarks>
public static class MediaUrl
{
    /// <summary>
    /// Префикс собственной раздачи портфолио: только он получает <c>?size=</c>.
    /// </summary>
    private const string PortfolioPrefix = "/api/media/portfolio/";

    /// <summary>
    /// Префикс собственной раздачи аватаров: только он получает <c>?size=</c>.
    /// </summary>
    private const string AvatarPrefix = "/api/media/avatars/";

    /// <summary>
    /// Допустимые варианты размера; набор — часть контракта с сервером.
    /// </summary>
    private static readonly string[] Sizes = ["sm", "md", "lg"];

    /// <summary>
    /// Приводит адрес картинки к абсолютному виду.
    /// </summary>
    /// <remarks>
    /// Абсолютный <c>http(s)</c> возвращается как есть: легаси-картинки лежат на чужих
    /// доменах, и подменять их хост нельзя. Корневой относительный путь склеивается
    /// с базовым адресом вместе с портом — именно поэтому база приходит с завершающим
    /// слэшем (<see cref="Api.BarberSearchApiOptions"/>).
    /// </remarks>
    /// <param name="baseAddress">Базовый адрес API с завершающим слэшем.</param>
    /// <param name="url">Адрес из ответа API.</param>
    /// <returns>Абсолютный адрес картинки.</returns>
    /// <exception cref="ArgumentException">Адрес пуст.</exception>
    public static string Resolve(Uri baseAddress, string url)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var trimmed = url.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return trimmed;
        }

        return new Uri(baseAddress, trimmed).ToString();
    }

    /// <summary>
    /// Добавляет параметр размера к своему адресу раздачи.
    /// </summary>
    /// <remarks>
    /// Возвращает исходную строку без изменений для внешних ссылок, чужих префиксов,
    /// адресов с готовым query или фрагментом и неизвестного размера: дописывать query
    /// в чужой URL нельзя, а сервер всё равно отдаст исходник.
    /// </remarks>
    /// <param name="url">Адрес картинки из ответа API.</param>
    /// <param name="size">Вариант размера: <c>sm</c>, <c>md</c> или <c>lg</c>.</param>
    /// <returns>Адрес с параметром размера либо исходный адрес.</returns>
    /// <exception cref="ArgumentException">Адрес пуст.</exception>
    public static string Resize(string url, string size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (size is null || !Sizes.Contains(size, StringComparer.Ordinal))
        {
            return url;
        }

        if (!IsOwnMedia(url) || url.Contains('?') || url.Contains('#'))
        {
            return url;
        }

        return string.Concat(url, "?size=", size);
    }

    /// <summary>
    /// Своя ли раздача: только у неё есть варианты размера.
    /// </summary>
    private static bool IsOwnMedia(string url) =>
        url.StartsWith(PortfolioPrefix, StringComparison.Ordinal)
        || url.StartsWith(AvatarPrefix, StringComparison.Ordinal);
}
