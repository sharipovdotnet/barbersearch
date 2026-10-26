using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Контракт <see cref="Error"/> (R-40): фабрики дают ожидаемые значения, свойства
/// <c>init</c>-only позволяют копировать ошибку через <c>with</c>, равенство — по значениям.
/// </summary>
/// <remarks>
/// <see cref="Error"/> живёт в <c>Shared</c>, поэтому тесты лежат в Application-проекте:
/// он уже ссылается на <c>Shared</c>. Тесты вызывают <c>with</c> из другой сборки — именно
/// этот сценарий был сломан, пока свойства были <c>{ get; }</c>.
/// </remarks>
public class ErrorTests
{
    [Fact]
    public void Factories_UseDefaultCodes_WhenCodeIsNull()
    {
        var notFound = Error.NotFound(null, "Missing.");
        var validation = Error.Validation(null, "Invalid.");
        var conflict = Error.Conflict(null, "Conflict.");
        var failure = Error.Failure(null, "Boom.");
        var unauthorized = Error.Unauthorized(null, "Authentication required.");
        var forbidden = Error.Forbidden(null, "Access denied.");

        // Литералы, а не FallbackErrorCodes: тест фиксирует сам проводной формат <c>error.*</c>,
        // а не только совпадение фабрики с константой.
        AssertError(notFound, "error.notFound", "Missing.", ErrorType.NOT_FOUND, invalidField: null);
        AssertError(validation, "error.validation", "Invalid.", ErrorType.VALIDATION, invalidField: null);
        AssertError(conflict, "error.conflict", "Conflict.", ErrorType.CONFLICT, invalidField: null);
        AssertError(failure, "error.failure", "Boom.", ErrorType.FAILURE, invalidField: null);
        AssertError(unauthorized, "error.unauthorized", "Authentication required.", ErrorType.UNAUTHORIZED, invalidField: null);
        AssertError(forbidden, "error.forbidden", "Access denied.", ErrorType.FORBIDDEN, invalidField: null);
    }

    [Fact]
    public void Factories_KeepProvidedCodeAndField()
    {
        var validation = Error.Validation("validation.email", "Invalid.", "email");
        var conflict = Error.Conflict("user.create.duplicate", "Duplicate.");

        AssertError(validation, "validation.email", "Invalid.", ErrorType.VALIDATION, "email");
        AssertError(conflict, "user.create.duplicate", "Duplicate.", ErrorType.CONFLICT, invalidField: null);
    }

    [Fact]
    public void NotFound_PutsIdentifierIntoInvalidField()
    {
        var id = Guid.NewGuid();

        var error = Error.NotFound("record.missing", "Missing.", id);

        AssertError(error, "record.missing", "Missing.", ErrorType.NOT_FOUND, id.ToString());
    }

    [Fact]
    public void WithInvalidField_ChangesOnlyThatField()
    {
        var original = Error.Validation("validation.email", "Invalid.", "email");

        var copy = original with { InvalidField = "other" };

        AssertError(copy, "validation.email", "Invalid.", ErrorType.VALIDATION, "other");
        AssertError(original, "validation.email", "Invalid.", ErrorType.VALIDATION, "email");
        Assert.NotEqual(original, copy);
    }

    [Fact]
    public void WithMessage_ChangesOnlyMessage()
    {
        var original = Error.Validation("validation.email", "Duplicate.", "email");

        var copy = original with { Message = "Already exists." };

        AssertError(copy, "validation.email", "Already exists.", ErrorType.VALIDATION, "email");
        AssertError(original, "validation.email", "Duplicate.", ErrorType.VALIDATION, "email");
        Assert.NotEqual(original, copy);
    }

    [Fact]
    public void Equality_UsesAllPropertyValues()
    {
        var original = Error.Validation("validation.email", "Invalid.", "email");
        var same = Error.Validation("validation.email", "Invalid.", "email");

        Assert.Equal(original, same);
        Assert.Equal(original.GetHashCode(), same.GetHashCode());

        Assert.NotEqual(original, same with { Code = "validation.other" });
        Assert.NotEqual(original, same with { Message = "Other." });
        Assert.NotEqual(original, same with { Type = ErrorType.FAILURE });
        Assert.NotEqual(original, same with { InvalidField = "phone" });
    }

    private static void AssertError(
        Error error,
        string code,
        string message,
        ErrorType type,
        string? invalidField)
    {
        Assert.Equal(code, error.Code);
        Assert.Equal(message, error.Message);
        Assert.Equal(type, error.Type);
        Assert.Equal(invalidField, error.InvalidField);
    }
}
