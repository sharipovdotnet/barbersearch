namespace BarberSearch.Application.PasswordResets;

/// <summary>
/// Сроки одноразового токена сброса пароля.
/// </summary>
/// <remarks>
/// Именованные константы вместо магических чисел: TTL ссылки, кулдаун переотправки и
/// ретенция уборки — три разных срока, и каждый живёт в одном месте.
/// </remarks>
public static class PasswordResetLifetime
{
    /// <summary>
    /// Срок жизни ссылки из письма: 30 минут. Короче 24 часов у подтверждения email:
    /// сброс пароля — более чувствительная операция.
    /// </summary>
    public static readonly TimeSpan TOKEN_LIFETIME = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Кулдаун повторной отправки: 60 секунд с момента выдачи активного токена.
    /// </summary>
    public static readonly TimeSpan RESEND_COOLDOWN = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Сколько хранить истёкшие и использованные строки: 30 дней после срока или потребления.
    /// </summary>
    public static readonly TimeSpan TOKEN_RETENTION = TimeSpan.FromDays(30);
}
