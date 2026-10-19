using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Проверки прав текущего пользователя на ресурс.
/// </summary>
/// <remarks>
/// Проверка живёт рядом с обработчиками, а не в <c>IAuthorizationHandler</c>: обработчик уже
/// загрузил агрегат, поэтому проверка владения не стоит дополнительного запроса, а ответ
/// <c>403</c> получается сам собой — <see cref="ErrorType.FORBIDDEN"/> маппится в HTTP-код
/// в <c>FailureProblemDetails</c> слоя Web.
/// </remarks>
public static class CurrentUserExtensions
{
    /// <summary>
    /// Может ли текущий пользователь управлять ресурсом, принадлежащим <paramref name="ownerId"/>.
    /// Администратор может всё, владелец — свой ресурс.
    /// </summary>
    /// <param name="currentUser">Текущий пользователь запроса.</param>
    /// <param name="ownerId">Идентификатор пользователя-владельца ресурса.</param>
    /// <returns>
    /// Успех, если операция разрешена, иначе <c>Failure</c> с <see cref="ErrorType.FORBIDDEN"/>.
    /// </returns>
    public static UnitResult<Failure> EnsureCanManage(this ICurrentUser currentUser, Guid ownerId)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (currentUser.IsAdmin || currentUser.UserId == ownerId)
        {
            return UnitResult.Success<Failure>();
        }

        return new Failure(Error.Forbidden(
            "auth.forbidden",
            "You are not allowed to perform this operation on this resource."));
    }
}
