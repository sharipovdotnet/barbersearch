using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Contracts.Common;
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
/// <para>
/// Анонимный запрос не бросает исключение (R-29): <see cref="RequireUserId"/> и проверки
/// владения возвращают <c>401 auth.unauthorized</c>, поэтому забытая проверка на эндпоинте,
/// который когда-нибудь станет анонимным (R-25), не превращается в <c>500</c>.
/// </para>
/// </remarks>
public static class CurrentUserExtensions
{
    /// <summary>
    /// Текст ошибки «операция над ресурсом запрещена».
    /// </summary>
    private const string FORBIDDEN_MESSAGE =
        "You are not allowed to perform this operation on this resource.";

    /// <summary>
    /// Возвращает идентификатор текущего пользователя или <c>401 auth.unauthorized</c>.
    /// </summary>
    /// <param name="currentUser">Текущий пользователь запроса.</param>
    /// <returns>
    /// Успех с идентификатором из claim'а <c>sub</c> либо <c>Failure</c>
    /// с <see cref="ErrorType.UNAUTHORIZED"/> для анонимного запроса.
    /// </returns>
    public static Result<Guid, Failure> RequireUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        return currentUser.TryGetUserId(out var userId)
            ? Result.Success<Guid, Failure>(userId)
            : Result.Failure<Guid, Failure>(new Failure(UnauthorizedError()));
    }

    /// <summary>
    /// Может ли текущий пользователь управлять ресурсом, принадлежащим <paramref name="ownerId"/>.
    /// Администратор может всё, владелец — свой ресурс.
    /// </summary>
    /// <param name="currentUser">Текущий пользователь запроса.</param>
    /// <param name="ownerId">Идентификатор пользователя-владельца ресурса.</param>
    /// <returns>
    /// Успех, если операция разрешена; <c>Failure</c> с <see cref="ErrorType.UNAUTHORIZED"/>
    /// для анонимного запроса; иначе <c>Failure</c> с <see cref="ErrorType.FORBIDDEN"/>.
    /// </returns>
    public static UnitResult<Failure> EnsureCanManage(this ICurrentUser currentUser, Guid ownerId)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        // Сначала аутентификация, потом роль: администратор без claim'а sub — всё равно аноним.
        var userId = currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        if (currentUser.IsAdmin)
        {
            return UnitResult.Success<Failure>();
        }

        if (userId.Value == ownerId)
        {
            return UnitResult.Success<Failure>();
        }

        return new Failure(Error.Forbidden(
            ApiErrorCodes.Auth.Forbidden,
            FORBIDDEN_MESSAGE));
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
    /// Успех, если пользователь — владелец профиля или администратор; <c>Failure</c>
    /// с <see cref="ErrorType.UNAUTHORIZED"/> для анонимного запроса; иначе <c>Failure</c>
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

        // Сначала аутентификация, потом роль: администратор без claim'а sub — всё равно аноним.
        var userId = currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        if (currentUser.IsAdmin)
        {
            return UnitResult.Success<Failure>();
        }

        var owned = await barberProfiles.IsOwnedByAsync(
            barberProfileId,
            userId.Value,
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
            ApiErrorCodes.Auth.Forbidden,
            FORBIDDEN_MESSAGE));
    }

    private static Error UnauthorizedError() => Error.Unauthorized(
        ApiErrorCodes.Auth.Unauthorized,
        "Authentication is required to perform this operation.");
}
