namespace BarberSearch.Domain.Salons;

/// <summary>
/// Состояние салона до/после проверки администратором.
/// </summary>
/// <remarks>
/// Это не видимость: <see cref="Salon.IsActive"/> по-прежнему означает «виден и участвует
/// в поиске», а публичная выдача требует оба условия —
/// <see cref="Salon.IsActive"/> и <see cref="SalonModerationStatus.Active"/>.
/// <para>
/// Исторические и админские салоны создаются сразу <see cref="Active"/>: модерация нужна
/// только self-service созданию барбером (<see cref="Salon.SubmitByOwner"/>).
/// </para>
/// </remarks>
public enum SalonModerationStatus
{
    /// <summary>
    /// Салон создан барбером и ждёт решения администратора; в поиске не участвует.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Салон одобрен (или создан администратором) и участвует в поиске, если виден.
    /// </summary>
    Active = 2,

    /// <summary>
    /// Администратор отклонил салон; причина лежит в <see cref="Salon.RejectionReason"/>.
    /// </summary>
    Rejected = 3
}
