using Microsoft.AspNetCore.ResponseCompression;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Настройки сжатия ответов.
/// </summary>
/// <remarks>
/// Сжимаются только текстовые форматы, которые отдаёт API: JSON (включая ProblemDetails),
/// XML, JavaScript/CSS и HTML Swagger UI. Изображения, шрифты и уже сжатые контейнеры
/// (PNG/JPEG/WebP, zip/gzip/brotli) в списке отсутствуют намеренно: их повторное сжатие
/// тратит CPU, не уменьшая тело ответа. Список точный, потому что подстановочные MIME-типы
/// (<c>image/*</c>, <c>text/*</c>) middleware не поддерживает.
/// <para>
/// <see cref="ResponseCompressionOptions.EnableForHttps"/> включён, потому что в проде API
/// доступен только по HTTPS: при выключенном флаге middleware не сжимал бы ни один ответ.
/// </para>
/// </remarks>
public static class ResponseCompressionSettings
{
    /// <summary>
    /// MIME-типы, которые разрешено сжимать.
    /// </summary>
    public static readonly IReadOnlyList<string> MimeTypes =
    [
        "application/json",
        "application/problem+json",
        "application/xml",
        "application/xhtml+xml",
        "application/javascript",
        "text/plain",
        "text/css",
        "text/html",
        "text/xml",
        "text/json",
        "text/javascript"
    ];

    /// <summary>
    /// Применяет настройки к параметрам middleware сжатия.
    /// </summary>
    /// <param name="options">Параметры, которые регистрирует <c>AddResponseCompression</c>.</param>
    public static void Configure(ResponseCompressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.EnableForHttps = true;
        options.MimeTypes = MimeTypes.ToArray();
    }
}
