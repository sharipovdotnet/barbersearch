using Shared;

namespace BarberSearch.Domain.Common;

/// <summary>
/// Проверка URL, которые домен принимает от клиента: фотографии портфолио,
/// сайт салона, аватар пользователя.
/// </summary>
/// <remarks>
/// Разрешён только абсолютный URI со схемой <c>http</c>/<c>https</c> и непустым хостом.
/// Схемы вроде <c>javascript:</c> и <c>data:</c> отклоняются: клиент, который подставит
/// такое значение в <c>src</c>/<c>href</c>, получил бы stored XSS. <c>userinfo</c>
/// запрещён отдельно — <c>https://user:pass@host/…</c> маскирует настоящий хост и
/// утекает учётные данные в логи и Referer. Пробелы по краям срезаются до проверки,
/// и агрегат хранит уже обрезанное значение; пустая строка в необязательном поле
/// означает <c>null</c>.
/// </remarks>
internal static class UrlValidator
{
    /// <summary>
    /// Проверяет URL и возвращает ошибку с переданными кодом и полем либо <c>null</c>,
    /// если значение корректно.
    /// </summary>
    /// <param name="url">Проверяемое значение; для необязательного поля допускается <c>null</c>.</param>
    /// <param name="code">Код ошибки вида <c>{aggregate}.{method}.{field}</c>.</param>
    /// <param name="field">Имя поля для <see cref="Error.InvalidField"/>.</param>
    /// <param name="required">
    /// Обязательно ли поле: пустое значение обязательного поля отклоняется, необязательного —
    /// нормализуется в <c>null</c>.
    /// </param>
    /// <param name="normalized">
    /// Обрезанное значение при успехе; <c>null</c> для пустого необязательного поля
    /// и при любой ошибке.
    /// </param>
    public static Error? Validate(
        string? url,
        string code,
        string field,
        bool required,
        out string? normalized)
    {
        normalized = null;

        // Trim до всех проверок: иначе « https://… » отклонялся бы как невалидный URI,
        // а Required-поле из одних пробелов считалось бы заполненным.
        var candidate = url?.Trim();

        if (string.IsNullOrEmpty(candidate))
        {
            return required
                ? Error.Validation(code, "URL cannot be empty.", field)
                : null;
        }

        if (candidate.Length > Constants.MAX_URL_LENGTH)
        {
            return Error.Validation(
                code,
                $"URL cannot be longer than {Constants.MAX_URL_LENGTH} characters.",
                field);
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return Error.Validation(code, "URL must be a valid absolute URI.", field);
        }

        if (!IsHttpScheme(uri.Scheme))
        {
            return Error.Validation(code, "URL scheme must be http or https.", field);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Error.Validation(code, "URL must not contain user credentials.", field);
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return Error.Validation(code, "URL must contain a host.", field);
        }

        normalized = candidate;

        return null;
    }

    private static bool IsHttpScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
