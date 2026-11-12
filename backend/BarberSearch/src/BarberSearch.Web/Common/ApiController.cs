using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Shared;

namespace BarberSearch.Web.Common;

/// <summary>
/// Базовый контроллер API: единая точка превращения <c>Result</c> обработчиков в ответ HTTP.
/// </summary>
/// <remarks>
/// Обработчики Application ничего не бросают для бизнес-правил — они возвращают
/// <c>Result&lt;T, Failure&gt;</c> / <c>UnitResult&lt;Failure&gt;</c>, поэтому каждый action
/// заканчивается вызовом одного из методов ниже, и разметка кодов состояния не расползается
/// по контроллерам.
/// </remarks>
public abstract class ApiController : ControllerBase
{
    /// <summary>
    /// Ответ на команду без результата: 204 при успехе, <see cref="ProblemDetails"/> при провале.
    /// </summary>
    protected ActionResult ToActionResult(UnitResult<Failure> result)
        => result.IsSuccess ? NoContent() : FailureProblemDetails.CreateResult(result.Error);

    /// <summary>
    /// Ответ на команду с результатом (или на запрос): 200 с телом при успехе,
    /// <see cref="ProblemDetails"/> при провале.
    /// </summary>
    protected ActionResult<TResponse> ToActionResult<TResponse>(Result<TResponse, Failure> result)
        => result.IsSuccess ? Ok(result.Value) : FailureProblemDetails.CreateResult(result.Error);

    /// <summary>
    /// Ответ на создание ресурса: 201 с идентификатором созданной сущности при успехе,
    /// <see cref="ProblemDetails"/> при провале. Заголовок <c>Location</c> не выставляется.
    /// </summary>
    /// <remarks>
    /// Правило R-42 от 2026-10-25: заголовок <c>Location</c> не выставляется, пока у созданного
    /// ресурса нет self-URI, читаемого сразу после создания. Пересмотрено 2026-11-10 (issue-83):
    /// такой URI появился у профиля барбера — <c>GET api/barber-profiles/{id}</c> отдаёт
    /// собственный профиль владельца независимо от верификации. Поэтому
    /// <c>POST api/barber-profiles</c> больше не идёт через этот метод: он строит
    /// <c>CreatedAtAction(nameof(Get), …)</c> и отвечает <c>201</c> с
    /// <c>Location: /api/barber-profiles/{id}</c>. Остальные create-эндпоинты правило
    /// по-прежнему описывает: <c>GET api/bookings</c> и <c>GET api/reviews</c> — только списки
    /// (созданный отзыв к тому же скрыт до публикации), у <c>salons</c> и <c>users</c> чтения
    /// по id нет, у <c>hair-services</c> и <c>specializations</c> — только публичные списки
    /// активных строк, <c>GET api/auth/me</c> — не ресурс по id, а публичный
    /// <c>GET api/barbers/{id}</c> отдаёт лишь активный и подтверждённый профиль и сразу после
    /// создания отвечает 404. Для них ссылка в никуда хуже её отсутствия.
    /// </remarks>
    protected ActionResult<CreatedResourceResponse> ToCreatedResult(Result<Guid, Failure> result)
        => result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new CreatedResourceResponse(result.Value))
            : FailureProblemDetails.CreateResult(result.Error);

    /// <summary>
    /// Разбирает query-параметр <c>force</c> пишущих ручек расписания.
    /// </summary>
    /// <remarks>
    /// Значение принимается строкой, а не <c>bool</c>: биндер <c>bool</c> на <c>?force=maybe</c>
    /// добавляет ошибку модели и отвечает <c>400</c>, а контракт этапа требует трактовать
    /// невалидное значение как <c>false</c> (отдельного <c>400</c> нет). Принимается только
    /// <c>true</c> без учёта регистра; всё остальное, включая пустую строку, — <c>false</c>.
    /// </remarks>
    /// <param name="force">Сырое значение query-параметра или <c>null</c>.</param>
    protected static bool ReadForce(string? force)
        => string.Equals(force, "true", StringComparison.OrdinalIgnoreCase);
}
