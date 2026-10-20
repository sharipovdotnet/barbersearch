namespace Shared;

public record Error
{
    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }
    public string? InvalidField { get; }

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
        => new(code ?? "record.not.found", message, ErrorType.NOT_FOUND, invalidField?.ToString());

    public static Error Validation(string? code, string message, string? invalidField = null)
        => new(code ?? "validation.error", message, ErrorType.VALIDATION, invalidField);

    public static Error Conflict(string? code, string message)
        => new(code ?? "value.is.conflict", message, ErrorType.CONFLICT);

    public static Error Failure(string? code, string message)
        => new(code ?? "failure", message, ErrorType.FAILURE);

    /// <summary>
    /// Ошибка аутентификации: пользователь не представился или учётные данные неверны.
    /// </summary>
    public static Error Unauthorized(string? code, string message)
        => new(code ?? "unauthorized", message, ErrorType.UNAUTHORIZED);

    /// <summary>
    /// Ошибка авторизации: пользователь известен, но прав на операцию нет.
    /// </summary>
    public static Error Forbidden(string? code, string message)
        => new(code ?? "forbidden", message, ErrorType.FORBIDDEN);

    public Failure ToFailure() => this;
}

public enum ErrorType
{
    /// <summary>
    /// Неизвестная ошибка.
    /// </summary>
    NONE,

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
