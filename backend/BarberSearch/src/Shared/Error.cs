namespace Shared;

/// <summary>
/// Коды ошибок по умолчанию: подставляются фабриками <see cref="Error"/>, когда вызывающий
/// не передал собственный код (<c>code ?? ...</c>).
/// </summary>
/// <remarks>
/// Формат — <c>error.{detail}</c>. Это отдельный реестр: он не входит в публичный
/// <c>ApiErrorCodes</c> (другой namespace и другой набор значений) и срабатывает только
/// при <c>code == null</c>, поэтому пересечения с доменными кодами нет.
/// </remarks>
public static class FallbackErrorCodes
{
    /// <summary>
    /// Запись не найдена; <c>404</c>.
    /// </summary>
    public const string NOT_FOUND = "error.notFound";

    /// <summary>
    /// Ошибка валидации; <c>400</c>.
    /// </summary>
    public const string VALIDATION = "error.validation";

    /// <summary>
    /// Конфликт с текущим состоянием ресурса; <c>409</c>.
    /// </summary>
    public const string CONFLICT = "error.conflict";

    /// <summary>
    /// Внутренняя ошибка сервера; <c>500</c>.
    /// </summary>
    public const string FAILURE = "error.failure";

    /// <summary>
    /// Пользователь не аутентифицирован; <c>401</c>.
    /// </summary>
    public const string UNAUTHORIZED = "error.unauthorized";

    /// <summary>
    /// Пользователь известен, но прав на операцию нет; <c>403</c>.
    /// </summary>
    public const string FORBIDDEN = "error.forbidden";
}

/// <summary>
/// Доменная ошибка: машинный код, человекочитаемое сообщение, тип и необязательное имя поля.
/// </summary>
/// <remarks>
/// Экземпляр создаётся только статическими фабриками: конструктор приватный, поэтому
/// обойти проверки фабрик снаружи нельзя. Свойства объявлены как <c>{ get; init; }</c>,
/// чтобы ошибку можно было скопировать, изменив одно поле, — <c>error with { InvalidField = "email" }</c>
/// работает и из другой сборки. Это <c>record</c>: равенство и хеш-код считаются по значениям
/// <see cref="Code"/>, <see cref="Message"/>, <see cref="Type"/> и <see cref="InvalidField"/>.
/// </remarks>
public record Error
{
    /// <summary>
    /// Публичный код ошибки — часть HTTP-контракта (например, <c>booking.confirm.invalidStatus</c>).
    /// </summary>
    public string Code { get; init; }

    /// <summary>
    /// Сообщение для человека (по-английски).
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Тип ошибки; определяет код состояния HTTP в <c>FailureProblemDetails.ToStatusCode</c>.
    /// </summary>
    public ErrorType Type { get; init; }

    /// <summary>
    /// Имя поля запроса, к которому относится ошибка; <c>null</c>, если ошибка не про поле.
    /// </summary>
    public string? InvalidField { get; init; }

    private Error(string code, string message, ErrorType type, string? invalidField = null)
    {
        Code = code;
        Message = message;
        Type = type;
        InvalidField = invalidField;
    }

    /// <summary>
    /// Ошибка «запись не найдена». Идентификатор кладётся в <see cref="InvalidField"/>.
    /// </summary>
    public static Error NotFound(string? code, string message, Guid? invalidField = null)
        => new(code ?? FallbackErrorCodes.NOT_FOUND, message, ErrorType.NOT_FOUND, invalidField?.ToString());

    /// <summary>
    /// Ошибка валидации поля.
    /// </summary>
    public static Error Validation(string? code, string message, string? invalidField = null)
        => new(code ?? FallbackErrorCodes.VALIDATION, message, ErrorType.VALIDATION, invalidField);

    /// <summary>
    /// Ошибка конфликта с текущим состоянием ресурса.
    /// </summary>
    public static Error Conflict(string? code, string message)
        => new(code ?? FallbackErrorCodes.CONFLICT, message, ErrorType.CONFLICT);

    /// <summary>
    /// Внутренняя ошибка сервера.
    /// </summary>
    public static Error Failure(string? code, string message)
        => new(code ?? FallbackErrorCodes.FAILURE, message, ErrorType.FAILURE);

    /// <summary>
    /// Ошибка аутентификации: пользователь не представился или учётные данные неверны.
    /// </summary>
    public static Error Unauthorized(string? code, string message)
        => new(code ?? FallbackErrorCodes.UNAUTHORIZED, message, ErrorType.UNAUTHORIZED);

    /// <summary>
    /// Ошибка авторизации: пользователь известен, но прав на операцию нет.
    /// </summary>
    public static Error Forbidden(string? code, string message)
        => new(code ?? FallbackErrorCodes.FORBIDDEN, message, ErrorType.FORBIDDEN);

    /// <summary>
    /// Оборачивает ошибку в <see cref="Failure"/> из одной ошибки.
    /// </summary>
    public Failure ToFailure() => this;
}

/// <summary>
/// Тип доменной ошибки. Определяет код состояния HTTP в <c>FailureProblemDetails.ToStatusCode</c>.
/// </summary>
/// <remarks>
/// Варианта «неизвестная ошибка» здесь намеренно нет: каждый тип обязан иметь явный маппинг,
/// чтобы новый тип ошибки не превращался молча в 500. Значения в БД не сохраняются.
/// </remarks>
public enum ErrorType
{
    /// <summary>
    /// Ошибка валидации.
    /// </summary>
    VALIDATION,

    /// <summary>
    /// Ошибка ничего не найдено.
    /// </summary>
    NOT_FOUND,

    /// <summary>
    /// Ошибка сервера.
    /// </summary>
    FAILURE,

    /// <summary>
    /// Ошибка конфликт.
    /// </summary>
    CONFLICT,

    /// <summary>
    /// Ошибка аутентификации.
    /// </summary>
    UNAUTHORIZED,

    /// <summary>
    /// Ошибка авторизации.
    /// </summary>
    FORBIDDEN,
}
