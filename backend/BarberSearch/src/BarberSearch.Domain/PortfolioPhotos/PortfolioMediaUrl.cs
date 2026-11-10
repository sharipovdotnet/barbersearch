namespace BarberSearch.Domain.PortfolioPhotos;

/// <summary>
/// Публичный URL загруженной фотографии портфолио: единая точка сборки и строгого разбора.
/// </summary>
/// <remarks>
/// <para>
/// Формат — <c>/api/media/portfolio/{profileId:D}/{mediaId:N}.{jpg|png|webp}</c>, то есть
/// <b>относительный</b> путь: браузер разрешает его от origin SPA, а <c>/api</c> уже
/// проксируется на API. Абсолютные внешние ссылки, добавленные старым JSON-эндпоинтом,
/// этим форматом не описываются: для них остаётся <see cref="BarberProfiles.BarberProfile.AddPhoto"/>.
/// </para>
/// <para>
/// Разбор намеренно строгий: ровно два сегмента после префикса, <c>Guid</c> в формате <c>D</c>,
/// имя файла — 32 hex-символа нижнего регистра и точка, расширение — только <c>jpg</c>,
/// <c>png</c> или <c>webp</c> в нижнем регистре. Символы <c>%</c>, <c>?</c>, <c>#</c>,
/// обратный слэш и любые пробелы запрещены: строка попадает в <c>src</c> и в файловый путь,
/// поэтому двойное декодирование или обход каталога недопустимы.
/// </para>
/// </remarks>
public static class PortfolioMediaUrl
{
    /// <summary>
    /// Длина имени файла без расширения: <c>Guid</c> в формате <c>N</c>.
    /// </summary>
    private const int MEDIA_ID_LENGTH = 32;

    /// <summary>
    /// Разрешённые расширения. Именно значения, а не имена файлов: тип определяется
    /// по сигнатуре, расширение — производное от неё.
    /// </summary>
    private static readonly string[] ALLOWED_EXTENSIONS = ["jpg", "png", "webp"];

    /// <summary>
    /// Разбирает публичный URL и проверяет, что он принадлежит профилю
    /// <paramref name="profileId"/>.
    /// </summary>
    /// <param name="url">Проверяемый URL.</param>
    /// <param name="profileId">Идентификатор профиля, которому должен принадлежать URL.</param>
    /// <param name="mediaId">
    /// Идентификатор медиафайла при успехе; <see cref="Guid.Empty"/> при ошибке.
    /// </param>
    /// <param name="extension">
    /// Расширение (<c>jpg</c>, <c>png</c>, <c>webp</c>) при успехе; пустая строка при ошибке.
    /// </param>
    /// <returns><c>true</c>, если URL соответствует шаблону именно этого профиля.</returns>
    public static bool TryParse(
        string url,
        Guid profileId,
        out Guid mediaId,
        out string extension)
    {
        mediaId = Guid.Empty;
        extension = string.Empty;

        if (profileId == Guid.Empty)
        {
            return false;
        }

        if (!TryParse(url, out var parsedProfileId, out mediaId, out extension))
        {
            return false;
        }

        if (parsedProfileId != profileId)
        {
            mediaId = Guid.Empty;
            extension = string.Empty;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Разбирает публичный URL без заранее известного профиля — для хранилища, которое
    /// получает только строку URL и должно восстановить путь к файлу.
    /// </summary>
    /// <param name="url">Проверяемый URL.</param>
    /// <param name="profileId">Идентификатор профиля из URL при успехе.</param>
    /// <param name="mediaId">Идентификатор медиафайла при успехе.</param>
    /// <param name="extension">Расширение при успехе.</param>
    /// <returns><c>true</c>, если URL соответствует шаблону.</returns>
    public static bool TryParse(
        string url,
        out Guid profileId,
        out Guid mediaId,
        out string extension)
    {
        profileId = Guid.Empty;
        mediaId = Guid.Empty;
        extension = string.Empty;

        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        var prefix = Constants.PORTFOLIO_MEDIA_PATH_PREFIX + "/";

        if (!url.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = url.AsSpan(prefix.Length);

        if (remainder.IsEmpty || !ContainsOnlySafeCharacters(remainder))
        {
            return false;
        }

        var separator = remainder.IndexOf('/');

        // Ровно два сегмента после префикса: пустой первый или второй — не наш URL.
        if (separator <= 0 || separator == remainder.Length - 1)
        {
            return false;
        }

        var profileSegment = remainder[..separator];
        var fileSegment = remainder[(separator + 1)..];

        if (fileSegment.IndexOf('/') >= 0)
        {
            return false;
        }

        if (!TryParseProfileId(profileSegment, out var parsedProfileId))
        {
            return false;
        }

        if (!TryParseMediaId(fileSegment, out var parsedMediaId, out var parsedExtension))
        {
            return false;
        }

        profileId = parsedProfileId;
        mediaId = parsedMediaId;
        extension = parsedExtension;

        return true;
    }

    /// <summary>
    /// Собирает публичный URL фотографии. Единственное место, где известен формат строки.
    /// </summary>
    /// <param name="profileId">Идентификатор профиля барбера.</param>
    /// <param name="mediaId">Идентификатор медиафайла (UUIDv7).</param>
    /// <param name="extension">Расширение <c>jpg</c>, <c>png</c> или <c>webp</c>.</param>
    public static string Build(Guid profileId, Guid mediaId, string extension)
        => $"{Constants.PORTFOLIO_MEDIA_PATH_PREFIX}/{profileId:D}/{mediaId:N}.{extension}";

    /// <summary>
    /// Запрещает символы, из которых получаются другие URL, декодирование или обход каталога.
    /// </summary>
    private static bool ContainsOnlySafeCharacters(ReadOnlySpan<char> value)
    {
        foreach (var symbol in value)
        {
            if (symbol is '%' or '?' or '#' or '\\' || char.IsWhiteSpace(symbol))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Проверяет сегмент профиля: канонический <c>Guid</c> в формате <c>D</c> нижнего регистра.
    /// </summary>
    private static bool TryParseProfileId(ReadOnlySpan<char> segment, out Guid profileId)
    {
        profileId = Guid.Empty;

        if (segment.Length != 36 || segment.IndexOf('.') >= 0)
        {
            return false;
        }

        if (!Guid.TryParseExact(segment, "D", out var parsedProfileId)
            || parsedProfileId == Guid.Empty)
        {
            return false;
        }

        // TryParseExact("D") принимает и верхний регистр; каноном считаем нижний,
        // иначе два разных URL описывали бы один и тот же файл.
        if (!segment.SequenceEqual(parsedProfileId.ToString("D")))
        {
            return false;
        }

        profileId = parsedProfileId;

        return true;
    }

    /// <summary>
    /// Проверяет имя файла: 32 hex-символа нижнего регистра, точка и разрешённое расширение.
    /// </summary>
    private static bool TryParseMediaId(
        ReadOnlySpan<char> segment,
        out Guid mediaId,
        out string extension)
    {
        mediaId = Guid.Empty;
        extension = string.Empty;

        var dot = segment.IndexOf('.');

        if (dot != MEDIA_ID_LENGTH)
        {
            return false;
        }

        var name = segment[..dot];

        foreach (var symbol in name)
        {
            if (!Uri.IsHexDigit(symbol) || char.IsUpper(symbol))
            {
                return false;
            }
        }

        if (!Guid.TryParseExact(name, "N", out var parsedMediaId) || parsedMediaId == Guid.Empty)
        {
            return false;
        }

        var extensionSpan = segment[(dot + 1)..];

        foreach (var allowedExtension in ALLOWED_EXTENSIONS)
        {
            if (extensionSpan.SequenceEqual(allowedExtension))
            {
                mediaId = parsedMediaId;
                extension = allowedExtension;

                return true;
            }
        }

        return false;
    }
}
