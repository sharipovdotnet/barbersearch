namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Очередь заявок на роль барбера в админской сводке.
/// </summary>
/// <remarks>
/// <see cref="PendingCount"/> — ровно <c>AdminBarberRequestStatus.Pending</c>:
/// <c>status = Pending</c>. Одобренные и отклонённые заявки в очередь не входят, поэтому
/// число совпадает с размером раздела «Заявки» при фильтре по умолчанию.
/// </remarks>
/// <param name="PendingCount">Сколько заявок ждёт решения.</param>
public record AdminSummaryBarberRequestsResponse(int PendingCount);
