using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.GetUnreadCount;

/// <summary>
/// Обработчик счётчика непрочитанных уведомлений.
/// </summary>
/// <remarks>
/// Считается только число строк: клиенту нужен бейдж, а не сами уведомления. Точное число
/// отдаётся целиком, «99+» рисует клиент — так бейдж и <c>aria-label</c> не разойдутся.
/// </remarks>
public sealed class GetUnreadCountHandler
    : IQueryHandler<UnreadNotificationsCountResponse, GetUnreadCountQuery>
{
    private readonly IBarberNotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;

    public GetUnreadCountHandler(
        IBarberNotificationRepository notifications,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(currentUser);

        _notifications = notifications;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает число непрочитанных уведомлений текущего барбера.
    /// </summary>
    public async Task<Result<UnreadNotificationsCountResponse, Failure>> Handle(
        GetUnreadCountQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var count = await _notifications.CountUnreadAsync(userId.Value, cancellationToken);
        if (count.IsFailure)
        {
            return count.Error;
        }

        return new UnreadNotificationsCountResponse(count.Value);
    }
}
