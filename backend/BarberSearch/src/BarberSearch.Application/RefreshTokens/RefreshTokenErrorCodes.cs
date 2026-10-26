namespace BarberSearch.Application.RefreshTokens;

/// <summary>
/// Коды ошибок ротации refresh-токена.
/// </summary>
/// <remarks>
/// Значения — часть публичного контракта (<c>auth.refresh.*</c>), поэтому менять их нельзя.
/// Здесь только те коды, которые повторяются в нескольких местах; коды, встречающиеся один раз
/// (<c>auth.refresh.revoked</c>, <c>auth.refresh.expired</c>, <c>auth.refresh.inactive</c>),
/// остаются на месте использования.
/// </remarks>
public static class RefreshTokenErrorCodes
{
    /// <summary>
    /// Refresh-токен не передан в запросе.
    /// </summary>
    public const string MISSING = "auth.refresh.missing";

    /// <summary>
    /// Refresh-токен неизвестен или не принадлежит активному пользователю.
    /// </summary>
    public const string INVALID = "auth.refresh.invalid";

    /// <summary>
    /// Заменённый refresh-токен предъявлен повторно.
    /// </summary>
    public const string REUSED = "auth.refresh.reused";
}
