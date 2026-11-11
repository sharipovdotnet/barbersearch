namespace BarberSearch.Application.EmailChanges;

/// <summary>
/// Сроки одноразового токена смены адреса электронной почты.
/// </summary>
/// <remarks>
/// Те же три срока, что у подтверждения email: TTL ссылки, кулдаун повторной заявки и
/// ретенция уборки. Именованные константы вместо магических чисел.
/// </remarks>
public static class EmailChangeLifetime
{
    /// <summary>
    /// Срок жизни ссылки из письма: 24 часа.
    /// </summary>
    public static readonly TimeSpan TOKEN_LIFETIME = TimeSpan.FromHours(24);

    /// <summary>
    /// Кулдаун повторной заявки: 60 секунд с момента выдачи активного токена.
    /// </summary>
    public static readonly TimeSpan RESEND_COOLDOWN = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Сколько хранить истёкшие и использованные строки: 30 дней после срока или потребления.
    /// </summary>
    public static readonly TimeSpan TOKEN_RETENTION = TimeSpan.FromDays(30);
}
