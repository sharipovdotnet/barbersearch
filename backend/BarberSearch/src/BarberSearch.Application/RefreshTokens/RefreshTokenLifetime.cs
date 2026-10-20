using CSharpFunctionalExtensions;
using Shared;
using RefreshTokenEntity = BarberSearch.Domain.RefreshTokens.RefreshToken;

namespace BarberSearch.Application.RefreshTokens;

/// <summary>
/// Проверка срока жизни refresh-токена по границам домена.
/// </summary>
internal static class RefreshTokenLifetime
{
    /// <summary>
    /// Ошибка, если срок вне диапазона 7–30 дней; иначе <c>null</c>.
    /// </summary>
    public static Failure? Validate(int lifetimeDays)
        => lifetimeDays is < RefreshTokenEntity.MIN_LIFETIME_DAYS
            or > RefreshTokenEntity.MAX_LIFETIME_DAYS
            ? new Failure(Error.Validation(
                "auth.refreshToken.lifetime",
                $"""
                 Refresh token lifetime must be between
                 {RefreshTokenEntity.MIN_LIFETIME_DAYS} and
                 {RefreshTokenEntity.MAX_LIFETIME_DAYS} days, got {lifetimeDays}.
                 """,
                nameof(lifetimeDays)))
            : null;
}
