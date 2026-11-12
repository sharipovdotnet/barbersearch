namespace BarberSearch.Web.Storage;

/// <summary>
/// MIME-тип файла изображения по расширению: общий для обоих провайдеров.
/// </summary>
/// <remarks>
/// Тип определяется расширением из media-URL, а не содержимым файла: расширение уже проверено
/// доменом (<c>PortfolioMediaUrl</c> / <c>UserAvatarMediaUrl</c>), а инспектор сигнатуры
/// работает на загрузке. В хранимых метаданных S3 <c>Content-Type</c> — производное значение,
/// поэтому раздача не зависит от того, чем объект записали.
/// </remarks>
internal static class PhotoContentTypes
{
    /// <summary>
    /// MIME-тип JPEG: расширение <c>jpg</c>.
    /// </summary>
    public const string JPEG = "image/jpeg";

    /// <summary>
    /// MIME-тип PNG: расширение <c>png</c>.
    /// </summary>
    public const string PNG = "image/png";

    /// <summary>
    /// MIME-тип WebP: расширение <c>webp</c> и все производные варианты.
    /// </summary>
    public const string WEBP = "image/webp";

    /// <summary>
    /// MIME-тип для неизвестного расширения: браузер не должен угадывать содержимое.
    /// </summary>
    public const string UNKNOWN = "application/octet-stream";

    /// <summary>
    /// Возвращает MIME-тип по расширению с точкой или без неё.
    /// </summary>
    /// <param name="extension">Расширение: <c>.jpg</c>, <c>jpg</c>, <c>png</c>, <c>webp</c>.</param>
    /// <returns>MIME-тип или <see cref="UNKNOWN"/> для всего остального.</returns>
    public static string For(string extension)
    {
        var normalized = (extension ?? string.Empty).Trim().TrimStart('.');

        if (string.Equals(normalized, "jpg", StringComparison.OrdinalIgnoreCase))
        {
            return JPEG;
        }

        if (string.Equals(normalized, "png", StringComparison.OrdinalIgnoreCase))
        {
            return PNG;
        }

        if (string.Equals(normalized, "webp", StringComparison.OrdinalIgnoreCase))
        {
            return WEBP;
        }

        return UNKNOWN;
    }
}
