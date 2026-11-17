using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Push.CleanupPushNotifications;

/// <summary>
/// Обработчик уборки: удаляет старые уведомления и давно не подтверждённые токены.
/// </summary>
/// <remarks>
/// <para>
/// Две границы, потому что у данных разный срок: уведомление старше <c>Push:RetentionDays</c>
/// (30 дней) не нужно никому, а токен старше <c>Push:DeviceTokenRetentionDays</c> (180 дней)
/// бесполезен — приложение на телефоне за полгода минимум раз перерегистрировалось бы.
/// Токены удаляются ещё и на снятие с регистрации и на отказ FCM, уборка — последняя страховка.
/// </para>
/// <para>
/// Некорректное значение конфигурации (ноль или отрицательное) заменяется значением по
/// умолчанию, а не удаляет всё подряд. Оба <c>DELETE</c> идемпотентны.
/// </para>
/// </remarks>
public sealed class CleanupPushNotificationsHandler
    : ICommandHandler<CleanupPushNotificationsResult, CleanupPushNotificationsCommand>
{
    private readonly IPushNotificationRepository _notifications;
    private readonly IPushDeviceTokenRepository _tokens;
    private readonly PushOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CleanupPushNotificationsHandler> _logger;

    public CleanupPushNotificationsHandler(
        IPushNotificationRepository notifications,
        IPushDeviceTokenRepository tokens,
        PushOptions options,
        TimeProvider timeProvider,
        ILogger<CleanupPushNotificationsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _notifications = notifications;
        _tokens = tokens;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Удаляет данные за границами ретенции.
    /// </summary>
    public async Task<Result<CleanupPushNotificationsResult, Failure>> Handle(
        CleanupPushNotificationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var retentionDays = _options.RetentionDays > 0
            ? _options.RetentionDays
            : PushOptions.DEFAULT_RETENTION_DAYS;

        var deviceTokenRetentionDays = _options.DeviceTokenRetentionDays > 0
            ? _options.DeviceTokenRetentionDays
            : PushOptions.DEFAULT_DEVICE_TOKEN_RETENTION_DAYS;

        var now = _timeProvider.GetUtcNow();

        var deletedNotifications = await _notifications.DeleteOlderThanAsync(
            now.AddDays(-retentionDays),
            cancellationToken);
        if (deletedNotifications.IsFailure)
        {
            return deletedNotifications.Error;
        }

        var deletedTokens = await _tokens.DeleteStaleAsync(
            now.AddDays(-deviceTokenRetentionDays),
            cancellationToken);
        if (deletedTokens.IsFailure)
        {
            return deletedTokens.Error;
        }

        if (deletedNotifications.Value > 0 || deletedTokens.Value > 0)
        {
            // Сводку по тику пишет воркер; здесь — подробность для Debug.
            _logger.LogDebug(
                "Push cleanup removed {Notifications} notifications and {Tokens} device tokens.",
                deletedNotifications.Value,
                deletedTokens.Value);
        }

        return new CleanupPushNotificationsResult(
            deletedNotifications.Value,
            deletedTokens.Value);
    }
}
