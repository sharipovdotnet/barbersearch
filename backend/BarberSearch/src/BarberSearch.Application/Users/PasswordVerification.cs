namespace BarberSearch.Application.Users;

/// <summary>
/// Результат проверки пароля.
/// </summary>
public enum PasswordVerification
{
    /// <summary>
    /// Пароль не подходит.
    /// </summary>
    Failed,

    /// <summary>
    /// Пароль подходит.
    /// </summary>
    Success,

    /// <summary>
    /// Пароль подходит, но хеш записан в устаревшем формате и его нужно пересчитать.
    /// </summary>
    SuccessRehashNeeded
}
