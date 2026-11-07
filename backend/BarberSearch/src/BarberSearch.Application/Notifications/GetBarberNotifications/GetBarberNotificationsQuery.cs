using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;

namespace BarberSearch.Application.Notifications.GetBarberNotifications;

/// <summary>
/// Запрос страницы инбокса уведомлений барбера.
/// </summary>
/// <remarks>
/// Барбера в запросе нет: получатель берётся из текущей сессии, поэтому запросить чужой
/// инбокс нечем. Размер страницы по умолчанию — 20 (как в остальных списках кабинета).
/// </remarks>
/// <param name="Cursor">Курсор следующей страницы; <c>null</c> — первая.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetBarberNotificationsQuery(
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<BarberNotificationResponse>>;
