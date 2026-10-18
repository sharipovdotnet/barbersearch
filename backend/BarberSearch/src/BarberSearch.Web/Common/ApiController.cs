using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Auth;
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
    /// Заголовок <c>Location</c> не выставляется: в API пока нет операций чтения, по которым
    /// можно было бы дать ссылку на созданный ресурс, а ссылка в никуда хуже её отсутствия.
    /// </remarks>
    protected ActionResult<CreatedResourceResponse> ToCreatedResult(Result<Guid, Failure> result)
        => result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new CreatedResourceResponse(result.Value))
            : FailureProblemDetails.CreateResult(result.Error);

    /// <summary>
    /// Совпадает ли пользователь из маршрута с тем, кто пришёл в запросе.
    /// </summary>
    /// <remarks>
    /// Проверка владения ресурсом пока живёт в контроллере: она нужна там, где маршрут несёт
    /// <c>{userId}</c>. Когда таких действий станет много, её место — обработчик требования
    /// <c>IAuthorizationHandler</c> с ресурсом, а не копия в каждом action.
    /// </remarks>
    protected bool IsCurrentUser(Guid userId)
        => User.FindFirst(ClaimsPrincipalFactory.SubClaim)?.Value == userId.ToString();

    /// <summary>
    /// Пришёл ли запрос от администратора.
    /// </summary>
    protected bool IsAdmin()
        => User.IsInRole(nameof(UserRole.Admin));
}
