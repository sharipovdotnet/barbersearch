using System.Collections.Generic;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Common;

/// <summary>
/// Вспомогательные методы для доменных операций.
/// </summary>
/// <remarks>
/// Собранные ошибки упаковываются в <see cref="Failure"/>: неявные преобразования из
/// <c>Failure</c> в <c>Result&lt;T, Failure&gt;</c> и <c>UnitResult&lt;Failure&gt;</c>
/// уже есть в CSharpFunctionalExtensions.
/// </remarks>
internal static class DomainErrors
{
    /// <summary>
    /// Пустой список для накопления ошибок. Агрегат валидирует все поля сразу
    /// и возвращает полный список проблем, а не первую из них.
    /// </summary>
    public static List<Error> Collect() => [];

    /// <summary>
    /// Успешная операция без результата.
    /// </summary>
    public static UnitResult<Failure> Ok()
        => UnitResult.Success<Failure>();

    /// <summary>
    /// Провал с одной ошибкой.
    /// </summary>
    public static UnitResult<Failure> Fail(Error error)
        => UnitResult.Failure(new Failure(error));

    /// <summary>
    /// Провал с несколькими ошибками.
    /// </summary>
    public static UnitResult<Failure> Fail(Failure failure)
        => UnitResult.Failure(failure);

    /// <summary>
    /// Ошибка «цена не может быть отрицательной», либо <c>null</c>, если цена корректна.
    /// </summary>
    public static Error? NegativePrice(decimal price, string code)
        => price < 0
            ? Error.Validation(code, "Price cannot be negative.", nameof(price))
            : null;

    /// <summary>
    /// Ошибка «длительность вне диапазона», либо <c>null</c>, если длительность корректна.
    /// </summary>
    public static Error? InvalidDuration(int durationMinutes, string code)
        => durationMinutes < Constants.MIN_DURATION_MINUTES ||
           durationMinutes > Constants.MAX_DURATION_MINUTES
            ? Error.Validation(
                code,
                $"""
                 Duration must be between {Constants.MIN_DURATION_MINUTES} 
                 and {Constants.MAX_DURATION_MINUTES} minutes, got {durationMinutes}.
                 """,
                nameof(durationMinutes))
            : null;
}
