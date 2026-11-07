using BarberSearch.Application.Abstractions;
using BarberSearch.Domain;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Notifications.CleanupOldNotifications;

/// <summary>
/// Обработчик уборки: удаляет уведомления старше <c>Reminders:RetentionDays</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ретенция — 180 дней (решение D6): инбокс не должен расти вечно, а напоминание старше
/// полугода не имеет смысла. Уборка идёт одним <c>DELETE</c> по границе
/// <c>created_at &lt; now - RetentionDays</c> и не трогает настройки.
/// </para>
/// <para>
/// Некорректное значение конфигурации (ноль или отрицательное) заменяется значением
/// по умолчанию, а не удаляет всё подряд.
/// </para>
/// </remarks>
public sealed class CleanupOldNotificationsHandler
    : ICommandHandler<CleanupOldNotificationsResult, CleanupOldNotificationsCommand>
{
    private readonly IBarberNotificationRepository _notifications;
    private readonly RemindersOptions _remindersOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CleanupOldNotificationsHandler> _logger;

    public CleanupOldNotificationsHandler(
        IBarberNotificationRepository notifications,
        RemindersOptions remindersOptions,
        TimeProvider timeProvider,
        ILogger<CleanupOldNotificationsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(remindersOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _notifications = notifications;
        _remindersOptions = remindersOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Удаляет уведомления, созданные раньше границы ретенции.
    /// </summary>
    public async Task<Result<CleanupOldNotificationsResult, Failure>> Handle(
        CleanupOldNotificationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var retentionDays = _remindersOptions.RetentionDays > 0
            ? _remindersOptions.RetentionDays
            : Constants.NOTIFICATION_RETENTION_DAYS;

        var cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);

        var deleted = await _notifications.DeleteOlderThanAsync(cutoff, cancellationToken);
        if (deleted.IsFailure)
        {
            return deleted.Error;
        }

        if (deleted.Value > 0)
        {
            // Сводку по тику пишет воркер; здесь — подробность для Debug.
            _logger.LogDebug(
                "Notifications older than {Cutoff} have been removed: {Deleted} rows.",
                cutoff,
                deleted.Value);
        }

        return new CleanupOldNotificationsResult(deleted.Value);
    }
}
