namespace BarberSearch.Web.Auth;

/// <summary>
/// Имена политик авторизации.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Только администратор.
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    /// Только барбер.
    /// </summary>
    public const string BarberOnly = "BarberOnly";

    /// <summary>
    /// Пользователь с подтверждённым адресом электронной почты.
    /// </summary>
    public const string VerifiedEmail = "VerifiedEmail";
}
