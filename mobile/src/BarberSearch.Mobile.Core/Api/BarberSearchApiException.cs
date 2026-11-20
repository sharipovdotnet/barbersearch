using System.Net;
using System.Text.Json;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Ответ API с ошибкой, разобранный по <c>ProblemDetails</c>.
/// </summary>
/// <remarks>
/// Сервер отвечает <c>application/problem+json</c>, кладёт доменные коды в расширение
/// <c>errorCodes</c>, а ошибки валидации — в <c>errors</c> по полям (см. <c>FailureProblemDetails</c>
/// в Web). Клиенту нужны коды: по ним экран выбирает сообщение, текст <c>detail</c> — для человека.
/// </remarks>
public sealed class BarberSearchApiException : Exception
{
    private static readonly Dictionary<string, string[]> NoValidationErrors = [];

    /// <summary>
    /// Создаёт исключение из разобранного ответа.
    /// </summary>
    /// <remarks>
    /// Конструктор внутренний: снаружи исключение собирается только из настоящего ответа
    /// (<see cref="FromResponseAsync"/>), а тестам он доступен через <c>InternalsVisibleTo</c>.
    /// <paramref name="conflicts"/> необязателен, чтобы прежние вызовы и тесты без конфликтов
    /// компилировались без правок: список конфликтов есть только у <c>409 barberSchedule.conflict</c>.
    /// </remarks>
    internal BarberSearchApiException(
        HttpStatusCode statusCode,
        string message,
        IReadOnlyList<string> errorCodes,
        IReadOnlyDictionary<string, string[]> validationErrors,
        IReadOnlyList<ScheduleConflictResponse>? conflicts = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCodes = errorCodes;
        ValidationErrors = validationErrors;
        Conflicts = conflicts ?? [];
    }

    /// <summary>
    /// Код состояния HTTP ответа.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Доменные коды ошибок из расширения <c>errorCodes</c>; пусто, если ответ не ProblemDetails.
    /// </summary>
    public IReadOnlyList<string> ErrorCodes { get; }

    /// <summary>
    /// Ошибки валидации по полям (<c>400</c>); пусто для остальных ответов.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    /// <summary>
    /// Визиты, которые выйдут за новые часы расписания, из расширения <c>conflicts</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Приходят только с <c>409 barberSchedule.conflict</c>: сервер отказывается сохранять
    /// правку, пока клиент не подтвердит её <c>force</c>. Экран показывает по этому списку
    /// диалог, поэтому телефон, email и цены сюда не попадают — контракт даёт ровно то, что
    /// нужно объяснить человеку.
    /// </para>
    /// <para>
    /// Пустой список — не «конфликтов нет», а «тело не распознано»: другой ответ, старая
    /// версия сервера или гонка. В этом случае диалог показывать нечем, и экран говорит,
    /// что записи изменились.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ScheduleConflictResponse> Conflicts { get; }

    /// <summary>
    /// Разбирает неуспешный ответ.
    /// </summary>
    /// <remarks>
    /// Тело может быть не JSON — прокси, HTML-страница ошибки, пустой ответ. Тогда остаётся
    /// только код состояния и стандартная причина: исключение всё равно должно быть брошено.
    /// </remarks>
    /// <param name="response">Неуспешный ответ.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    internal static async Task<BarberSearchApiException> FromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        ProblemPayload? problem = null;

        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            problem = await JsonSerializer
                .DeserializeAsync<ProblemPayload>(stream, ApiJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Не ProblemDetails: причина ответа остаётся на уровне кода состояния.
        }

        var message = problem?.Detail
            ?? problem?.Title
            ?? $"API responded with {(int)response.StatusCode} {response.ReasonPhrase}.";

        return new BarberSearchApiException(
            response.StatusCode,
            message,
            problem?.ErrorCodes ?? [],
            problem?.Errors ?? NoValidationErrors,
            problem?.Conflicts);
    }

    /// <summary>
    /// Поля ProblemDetails, которые нужны клиенту: массив кодов, карта ошибок по полям и конфликты.
    /// </summary>
    /// <remarks>
    /// <c>conflicts</c> разбирается тем же сериализатором: перечисление статуса приходит
    /// строкой, как и во всём API. Неразобранный массив не отменяет коды ошибок — иначе
    /// <c>409</c> перестал бы опознаваться как конфликт расписания.
    /// </remarks>
    private sealed record ProblemPayload(
        string? Title,
        string? Detail,
        int? Status,
        string[]? ErrorCodes,
        Dictionary<string, string[]>? Errors,
        ScheduleConflictResponse[]? Conflicts);
}
