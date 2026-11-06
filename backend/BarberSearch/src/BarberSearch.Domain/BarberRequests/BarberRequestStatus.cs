namespace BarberSearch.Domain.BarberRequests;

/// <summary>
/// Статус заявки клиента на роль барбера.
/// </summary>
/// <remarks>
/// Решение по заявке принимается один раз: из <see cref="Pending"/> заявка переходит либо
/// в <see cref="Approved"/>, либо в <see cref="Rejected"/>. «Пустого» значения нет:
/// каждая строка таблицы описывает конкретную заявку, а не черновик.
/// </remarks>
public enum BarberRequestStatus
{
    /// <summary>Заявка подана и ждёт решения администратора.</summary>
    Pending = 1,

    /// <summary>Заявка одобрена: пользователь получил роль <c>Barber</c>.</summary>
    Approved = 2,

    /// <summary>Заявка отклонена с причиной.</summary>
    Rejected = 3
}
