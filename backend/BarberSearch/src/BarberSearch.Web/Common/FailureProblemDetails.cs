using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared;

namespace BarberSearch.Web.Common;

/// <summary>
/// Превращает доменный <see cref="Failure"/> в <see cref="ProblemDetails"/> с кодом состояния HTTP.
/// </summary>
/// <remarks>
/// Соответствие зафиксировано в AGENT.md §7.5: <c>VALIDATION</c> → 400, <c>UNAUTHORIZED</c> → 401,
/// <c>FORBIDDEN</c> → 403, <c>NOT_FOUND</c> → 404, <c>CONFLICT</c> → 409, <c>FAILURE</c> → 500.
/// Маппинг живёт в контроллерах, а не в middleware:
/// обработчики Application возвращают <c>Result</c>, поэтому объект <see cref="Failure"/> до
/// middleware не доходит, и middleware увидел бы только исключения.
/// </remarks>
public static class FailureProblemDetails
{
    /// <summary>
    /// Имя расширения <see cref="ProblemDetails"/>, в котором едут доменные коды ошибок
    /// (<c>barberProfile.addPhoto.duplicate</c> и т. п.) — по ним клиент отличает причины,
    /// не разбирая текст сообщения.
    /// </summary>
    public const string ERROR_CODES_EXTENSION = "errorCodes";

    /// <summary>
    /// Имя расширения <see cref="ProblemDetails"/>, в котором едет список затронутых визитов
    /// ответа <c>409 barberSchedule.conflict</c>.
    /// </summary>
    public const string CONFLICTS_EXTENSION = "conflicts";

    /// <summary>
    /// Код состояния HTTP для типа доменной ошибки. Маппинг явный для каждого значения
    /// <see cref="ErrorType"/>: новый тип ошибки не должен молча превращаться в 500.
    /// </summary>
    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.VALIDATION => StatusCodes.Status400BadRequest,
        ErrorType.UNAUTHORIZED => StatusCodes.Status401Unauthorized,
        ErrorType.FORBIDDEN => StatusCodes.Status403Forbidden,
        ErrorType.NOT_FOUND => StatusCodes.Status404NotFound,
        ErrorType.CONFLICT => StatusCodes.Status409Conflict,
        ErrorType.FAILURE => StatusCodes.Status500InternalServerError,
        // Защита от значения вне объявленного перечисления (например, приведённого числа):
        // контракт не должен падать на неопределённом значении, поэтому отвечаем 500.
        _ => StatusCodes.Status500InternalServerError
    };

    /// <summary>
    /// Собирает ответ с ошибкой. Готовый <see cref="ObjectResult"/> нужен, чтобы результат
    /// подходил и для <c>ActionResult</c>, и для <c>ActionResult&lt;T&gt;</c>.
    /// </summary>
    /// <param name="failure">Провал сценария: одна ошибка или несколько.</param>
    public static ObjectResult CreateResult(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var errors = failure.Errors;

        if (errors.Count == 0)
        {
            return new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = TitleFor(StatusCodes.Status500InternalServerError),
                Detail = "The operation failed without reporting an error."
            });
        }

        // В наборе могут быть ошибки разных типов; побеждает самая «тяжёлая» — максимальный
        // код состояния. Для однородных наборов (обычный случай) результат очевиден.
        var statusCode = errors.Select(error => ToStatusCode(error.Type)).Max();

        var problemDetails = statusCode == StatusCodes.Status400BadRequest
            ? new ValidationProblemDetails(BuildValidationErrors(errors))
            : new ProblemDetails();

        problemDetails.Status = statusCode;
        problemDetails.Title = TitleFor(statusCode);
        problemDetails.Detail = string.Join(" ", errors.Select(error => error.Message));
        problemDetails.Extensions[ERROR_CODES_EXTENSION] = errors.Select(error => error.Code).ToArray();

        return new ObjectResult(problemDetails);
    }

    /// <summary>
    /// Собирает <c>409</c> расписания: заголовок и код ошибки те же, что у остальных
    /// конфликтов, плюс расширение <c>conflicts</c> со списком затронутых визитов.
    /// </summary>
    /// <remarks>
    /// Фабрика одна на все пять пишущих ручек расписания: заголовок 409 и <c>errorCodes</c>
    /// не должны дублироваться в контроллерах. Отдельный флаг нужен из-за разного текста
    /// <c>detail</c>: «вне новых часов» и «внутри отпуска» — разные объяснения для человека,
    /// а клиент известные коды всё равно не показывает.
    /// </remarks>
    /// <param name="conflicts">Затронутые визиты; пустой список сюда не передаётся.</param>
    /// <param name="insideTimeOff">
    /// <c>true</c> — конфликт вызван отпуском (<c>POST time-offs</c>), иначе — новыми часами.
    /// </param>
    public static ObjectResult CreateConflictResult(
        IReadOnlyList<ScheduleConflictResponse> conflicts,
        bool insideTimeOff = false)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = TitleFor(StatusCodes.Status409Conflict),
            Detail = BuildConflictDetail(conflicts.Count, insideTimeOff)
        };

        problemDetails.Extensions[ERROR_CODES_EXTENSION] = new[] { ApiErrorCodes.Schedule.Conflict };
        problemDetails.Extensions[CONFLICTS_EXTENSION] = conflicts;

        return new ObjectResult(problemDetails);
    }

    /// <summary>
    /// Английский текст конфликта: число и форма — по фактическому количеству визитов.
    /// </summary>
    private static string BuildConflictDetail(int count, bool insideTimeOff)
    {
        var bookings = count == 1 ? "1 booking" : $"{count} bookings";

        return insideTimeOff
            ? $"{bookings} {(count == 1 ? "falls" : "fall")} inside the time off."
            : $"{bookings} {(count == 1 ? "falls" : "fall")} outside the new working hours.";
    }

    /// <summary>
    /// Раскладывает ошибки валидации по полям: ключ — <see cref="Error.InvalidField"/>,
    /// а если его нет, то код ошибки. Так клиент показывает сообщение рядом с нужным полем.
    /// </summary>
    private static Dictionary<string, string[]> BuildValidationErrors(IReadOnlyList<Error> errors)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            var key = string.IsNullOrWhiteSpace(error.InvalidField) ? error.Code : error.InvalidField;

            result[key] = result.TryGetValue(key, out var existing)
                ? [.. existing, error.Message]
                : [error.Message];
        }

        return result;
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "One or more validation errors occurred.",
        StatusCodes.Status401Unauthorized => AuthConstants.AUTHENTICATION_FAILED_TITLE,
        StatusCodes.Status403Forbidden => AuthConstants.ACCESS_DENIED_TITLE,
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state of the resource.",
        _ => "An unexpected error occurred while processing the request."
    };
}
