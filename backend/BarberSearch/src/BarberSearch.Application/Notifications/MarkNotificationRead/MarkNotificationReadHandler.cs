using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.MarkNotificationRead;

/// <summary>
/// Обработчик отметки «прочитано» для одного уведомления.
/// </summary>
/// <remarks>
/// <para>
/// Чужое уведомление отвечает тем же <c>404 notification.notFound</c>, что и несуществующее:
/// различать «нет такого» и «это чужое» нельзя, иначе проверка существования чужого инбокса
/// становится утечкой. Поэтому после загрузки сущности обработчик сравнивает
/// <c>UserId</c> с текущим пользователем, а не полагается на репозиторий.
/// </para>
/// <para>
/// Момент прочтения приходит из <see cref="TimeProvider"/>: домен бизнес-«сейчас» не читает
/// (AGENT.md §7.6), и в тестах время двигается без ожидания.
/// </para>
/// </remarks>
public sealed class MarkNotificationReadHandler : ICommandHandler<MarkNotificationReadCommand>
{
    private readonly IBarberNotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public MarkNotificationReadHandler(
        IBarberNotificationRepository notifications,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _notifications = notifications;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Помечает уведомление прочитанным, если оно принадлежит текущему барберу.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        MarkNotificationReadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var notificationResult = await _notifications.GetByIdAsync(
            command.NotificationId,
            cancellationToken);
        if (notificationResult.IsFailure)
        {
            return notificationResult.Error;
        }

        var notification = notificationResult.Value;

        if (notification.UserId != userId.Value)
        {
            return NotFound(command.NotificationId);
        }

        var marked = notification.MarkRead(_timeProvider.GetUtcNow());
        if (marked.IsFailure)
        {
            return marked.Error;
        }

        return await _notifications.UpdateAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Ошибка «уведомления нет» — общая для отсутствующей и чужой строки.
    /// </summary>
    private static Failure NotFound(Guid notificationId)
        => new(Error.NotFound(
            ApiErrorCodes.Notification.NotFound,
            $"Notification '{notificationId}' was not found.",
            notificationId));
}
