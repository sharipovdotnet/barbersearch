using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
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

    /// <summary>
    /// Может ли текущий пользователь действовать от имени барбера — владельца профиля,
    /// к которому относится ресурс (например, бронь).
    /// </summary>
    /// <param name="currentUser">Текущий пользователь запроса.</param>
    /// <param name="barberProfiles">
    /// Репозиторий профилей: владение проверяется одним <c>EXISTS</c>-запросом, без загрузки агрегата.
    /// </param>
    /// <param name="barberProfileId">Профиль барбера, к которому относится ресурс.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Успех, если пользователь — владелец профиля или администратор, иначе <c>Failure</c>
    /// с <see cref="ErrorType.FORBIDDEN"/>.
    /// </returns>
    public static async Task<UnitResult<Failure>> EnsureIsBarberOfAsync(
        this ICurrentUser currentUser,
        IBarberProfileRepository barberProfiles,
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(barberProfiles);

        if (currentUser.IsAdmin)
        {
            return UnitResult.Success<Failure>();
        }

        var owned = await barberProfiles.IsOwnedByAsync(
            barberProfileId,
            currentUser.UserId,
            cancellationToken);
        if (owned.IsFailure)
        {
            return owned.Error;
        }

        if (owned.Value)
        {
            return UnitResult.Success<Failure>();
        }

        return new Failure(Error.Forbidden(
            "auth.forbidden",
            "You are not allowed to perform this operation on this resource."));
    }
}
