using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.MarkAllNotificationsRead;

/// <summary>
/// Обработчик «прочитать все»: один UPDATE по непрочитанным строкам барбера.
/// </summary>
/// <remarks>
/// <para>
/// Отметка идёт одним запросом в репозитории, а не загрузкой всех строк: инбокс может быть
/// длинным, а операция — частой (кнопка «Прочитать все»). <c>read_at</c> уже прочитанных
/// строк не перезаписывается, поэтому повторный вызов ничего не меняет.
/// </para>
/// <para>
/// Момент прочтения — из <see cref="TimeProvider"/>: «сейчас» в Application, а не в домене
/// (AGENT.md §7.6).
/// </para>
/// </remarks>
public sealed class MarkAllNotificationsReadHandler
    : ICommandHandler<MarkAllNotificationsReadCommand>
{
    private readonly IBarberNotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public MarkAllNotificationsReadHandler(
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
    /// Помечает все непрочитанные уведомления текущего барбера прочитанными.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        MarkAllNotificationsReadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var marked = await _notifications.MarkAllReadAsync(
            userId.Value,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        return marked.IsFailure
            ? marked.Error
            : UnitResult.Success<Failure>();
    }
}
