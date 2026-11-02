namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Выключенные салоны в админской сводке.
/// </summary>
/// <remarks>
/// <see cref="InactiveCount"/> — ровно <c>AdminCatalogueStatus.Hidden</c> для салонов:
/// <c>is_active = false</c>. Выключенный салон не участвует в поиске и не показывается
/// клиентам, но связи барберов с ним сохраняются до включения обратно.
/// </remarks>
/// <param name="InactiveCount">Сколько салонов выключено.</param>
public record AdminSummarySalonsResponse(int InactiveCount);
