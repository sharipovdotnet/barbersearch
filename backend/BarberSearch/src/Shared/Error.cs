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

    public static Error NotFound(string? code, string message, Guid guid)
        => new(code ?? "record.not.found", message, ErrorType.NOT_FOUND);
    
    public static Error Validation(string? code, string message, string? invalidField = null)
        => new(code ?? "validation.error", message, ErrorType.VALIDATION, invalidField);
    
    public static Error Conflict(string? code, string? message)
        => new(code ?? "value.is.conflict", message,  ErrorType.CONFLICT);
    
    public static Error Failure(string? code, string message)
        => new(code ?? "failure", message, ErrorType.FAILURE);

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
}