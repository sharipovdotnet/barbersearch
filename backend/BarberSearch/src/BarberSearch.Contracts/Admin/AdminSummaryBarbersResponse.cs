namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Барберы без подтверждения в админской сводке.
/// </summary>
/// <remarks>
/// <see cref="UnverifiedCount"/> — ровно <c>AdminBarberStatus.Pending</c>:
/// <c>is_verified = false</c> без условия <c>is_active</c>. Добавить <c>is_active</c> значило бы
/// разойтись с очередью «На проверке»: скрытый неподтверждённый профиль из очереди не исчезает.
/// </remarks>
/// <param name="UnverifiedCount">Сколько профилей ждёт подтверждения.</param>
public record AdminSummaryBarbersResponse(int UnverifiedCount);
