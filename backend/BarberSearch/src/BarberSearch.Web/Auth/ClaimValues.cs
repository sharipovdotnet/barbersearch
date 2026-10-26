namespace BarberSearch.Web.Auth;

/// <summary>
/// Строковые значения claim'ов, которые читаются политиками авторизации.
/// </summary>
/// <remarks>
/// <c>email_verified</c> хранится строкой, потому что claim в JWT — всегда строка.
/// Одно значение записывается в <see cref="ClaimsPrincipalFactory"/> и проверяется
/// в <see cref="AuthorizationExtensions"/>; литералы в двух местах разошлись бы молча.
/// </remarks>
public static class ClaimValues
{
    /// <summary>
    /// Подтверждено/разрешено.
    /// </summary>
    public const string TRUE = "true";

    /// <summary>
    /// Не подтверждено/запрещено.
    /// </summary>
    public const string FALSE = "false";
}
