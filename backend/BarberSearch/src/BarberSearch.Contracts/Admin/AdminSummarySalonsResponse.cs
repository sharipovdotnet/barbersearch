namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Салоны, требующие внимания, в админской сводке: два независимых счётчика.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InactiveCount"/> — ровно <c>AdminCatalogueStatus.Hidden</c> для салонов:
/// <c>is_active = false AND moderation_status = Active</c>. Выключенный салон не участвует
/// в поиске и не показывается клиентам, но связи барберов с ним сохраняются до включения
/// обратно.
/// </para>
/// <para>
/// <see cref="PendingModerationCount"/> — ровно <c>AdminCatalogueStatus.Pending</c>:
/// <c>moderation_status = Pending</c> без условия <c>is_active</c>. Это не «выключенные»
/// салоны, хотя салон на проверке тоже неактивен: он ждёт решения, а не скрыт
/// администратором.
/// </para>
/// </remarks>
/// <param name="InactiveCount">Сколько салонов выключено.</param>
/// <param name="PendingModerationCount">Сколько салонов ждёт модерации.</param>
public record AdminSummarySalonsResponse(int InactiveCount, int PendingModerationCount);
