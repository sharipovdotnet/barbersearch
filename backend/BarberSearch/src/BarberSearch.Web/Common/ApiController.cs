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
    /// <see cref="ProblemDetails"/> при провале.
    /// </summary>
    /// <remarks>
    /// Заголовок <c>Location</c> сознательно не выставляется — решение R-42 от 2026-10-25:
    /// ни у одного из восьми create-эндпоинтов нет self-URI, читаемого сразу после создания.
    /// <c>GET api/bookings</c> и <c>GET api/reviews</c> — только списки (созданный отзыв к тому же
    /// скрыт до публикации), у <c>salons</c> и <c>users</c> чтения нет, у <c>hair-services</c>
    /// и <c>specializations</c> есть только публичные списки активных строк
    /// (<c>GET api/hair-services</c>, <c>GET api/specializations</c>) без чтения по id,
    /// <c>GET api/auth/me</c> — не ресурс по id, а единственный GET по id
    /// среди них, <c>GET api/barbers/{id}</c>, отдаёт лишь активный и подтверждённый профиль
    /// (<c>GetCardByIdAsync</c>: <c>IsActive &amp;&amp; IsVerified</c>) и сразу после создания
    /// отвечает 404. Ссылка в никуда хуже её отсутствия. Пересмотреть, когда у созданного ресурса
    /// появится GET по id: тогда здесь <c>CreatedAtAction</c> / <c>Created(uri, value)</c>.
    /// </remarks>
    protected ActionResult<CreatedResourceResponse> ToCreatedResult(Result<Guid, Failure> result)
        => result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new CreatedResourceResponse(result.Value))
            : FailureProblemDetails.CreateResult(result.Error);
}
