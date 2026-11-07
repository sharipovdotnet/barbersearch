using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.GetBarberNotifications;

/// <summary>
/// Обработчик страницы инбокса: свежие уведомления сверху, keyset-курсор.
/// </summary>
/// <remarks>
/// <para>
/// Получатель — текущий пользователь из сессии. Политика <c>BarberOnly</c> уже отсекла
/// клиента, а <c>RequireUserId</c> закрывает анонимный запрос <c>401</c>, поэтому ни
/// query-параметра с идентификатором барбера, ни проверки владения здесь нет: чужих строк
/// в выборке быть не может.
/// </para>
/// <para>
/// Порядок — <c>created_at DESC, id DESC</c>: тик воркера создаёт несколько уведомлений
/// в одну секунду, поэтому второй ключ обязателен и в курсоре, и в запросе.
/// </para>
/// </remarks>
public sealed class GetBarberNotificationsHandler
    : IQueryHandler<PagedResponse<BarberNotificationResponse>, GetBarberNotificationsQuery>
{
    private readonly IBarberNotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;

    public GetBarberNotificationsHandler(
        IBarberNotificationRepository notifications,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(currentUser);

        _notifications = notifications;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает страницу инбокса текущего барбера.
    /// </summary>
    public async Task<Result<PagedResponse<BarberNotificationResponse>, Failure>> Handle(
        GetBarberNotificationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(
                pageSize,
                ApiErrorCodes.Notification.PageSizePrefix) is { } pageSizeError)
        {
            return pageSizeError;
        }

        BarberNotificationCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = BarberNotificationCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _notifications.GetPageAsync(
            userId.Value,
            cursor,
            pageSize + 1,
            cancellationToken);
        if (rowsResult.IsFailure)
        {
            return rowsResult.Error;
        }

        var rows = rowsResult.Value;
        var hasNextPage = rows.Count > pageSize;
        var pageRows = hasNextPage ? rows.Take(pageSize).ToList() : rows.ToList();

        var items = pageRows
            .Select(NotificationMapping.ToResponse)
            .ToList();

        var nextCursor = hasNextPage && items.Count > 0
            ? new BarberNotificationCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<BarberNotificationResponse>(items, nextCursor);
    }
}
