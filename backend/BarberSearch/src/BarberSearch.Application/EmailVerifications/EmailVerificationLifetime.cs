namespace BarberSearch.Application.EmailVerifications;

/// <summary>
/// Сроки одноразового токена подтверждения email.
/// </summary>
/// <remarks>
/// Именованные константы вместо магических чисел: TTL письма, кулдаун переотправки и
/// ретенция уборки — три разных срока, и каждый живёт в одном месте.
/// </remarks>
public static class EmailVerificationLifetime
{
    /// <summary>
    /// Срок жизни ссылки из письма: 24 часа.
    /// </summary>
    public static readonly TimeSpan TOKEN_LIFETIME = TimeSpan.FromHours(24);

    /// <summary>
    /// Кулдаун повторной отправки: 60 секунд с момента выдачи активного токена.
    /// </summary>
    public static readonly TimeSpan RESEND_COOLDOWN = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Сколько хранить истёкшие и использованные строки: 30 дней после срока или потребления.
    /// </summary>
    public static readonly TimeSpan TOKEN_RETENTION = TimeSpan.FromDays(30);
}
