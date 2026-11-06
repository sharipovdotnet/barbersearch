namespace BarberSearch.Application.BarberRequests.Admin;

/// <summary>
/// Фильтр администраторского списка заявок в барберы.
/// </summary>
/// <remarks>
/// <see cref="Pending"/> — очередь решений (по умолчанию): только заявки в статусе
/// <c>Pending</c>, старые сверху (FIFO). <see cref="All"/> — любая заявка, свежие сверху:
/// по этому списку администратор проверяет, что уже было решено. Направление сортировки
/// задаёт статус, а не содержимое курсора.
/// </remarks>
public enum AdminBarberRequestStatus
{
    /// <summary>Очередь решений: только <c>Pending</c>, старые сверху.</summary>
    Pending = 0,

    /// <summary>Все заявки в любом статусе, свежие сверху.</summary>
    All = 1
}
