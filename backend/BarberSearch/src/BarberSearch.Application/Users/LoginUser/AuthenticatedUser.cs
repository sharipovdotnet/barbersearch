using BarberSearch.Domain.Users;

namespace BarberSearch.Application.Users.LoginUser;

/// <summary>
/// Подтверждённая личность вошедшего пользователя — то, из чего слой Web строит claims.
/// </summary>
/// <remarks>
/// Наружу отдаётся только то, что нужно для токена и cookie; хеш пароля сюда не попадает.
/// </remarks>
public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    UserRole Role,
    Guid? BarberProfileId,
    bool IsEmailVerified,
    string SecurityStamp)
{
    /// <summary>
    /// Снимок агрегата <see cref="User"/> для выдачи учётных данных.
    /// </summary>
    public static AuthenticatedUser From(User user)
        => new(
            user.Id,
            user.Email,
            user.Role,
            user.BarberProfileId,
            user.IsEmailVerified,
            user.SecurityStamp);
}
