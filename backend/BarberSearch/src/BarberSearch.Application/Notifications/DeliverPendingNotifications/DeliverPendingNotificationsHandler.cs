using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Notifications.DeliverPendingNotifications;

/// <summary>
/// Обработчик тика доставки: отправляет пачку <c>Pending</c>-уведомлений в Telegram.
/// </summary>
/// <remarks>
/// <para>
/// Порядок решений повторяет §3.2 спецификации. Сначала — актуальность повода: если бронь
/// между созданием и отправкой отменили, завершили или отметили неявкой, уведомление
/// получает <c>Skipped</c> с причиной <c>booking is not confirmed</c>, и сообщение не уходит.
/// Затем — канал: без привязки или с выключенными напоминаниями строка тоже <c>Skipped</c>,
/// но остаётся в инбоксе.
/// </para>
/// <para>
/// Исход отправки различает два вида ошибок (<see cref="ITelegramSender"/>): «чат недоступен»
/// (<c>400</c>/<c>403</c> — бот заблокирован) даёт <c>Skipped</c> и <c>UnlinkTelegram()</c>,
/// чтобы не долбить мёртвый чат; всё остальное — <c>MarkFailed</c> и повтор по расписанию
/// 1/5/15 минут, максимум пять попыток. На пятой неудаче уведомление окончательно
/// <c>Failed</c> — это решает домен.
/// </para>
/// <para>
/// Текст собирается здесь и не содержит имени, телефона и заметок клиента (решение D3,
/// 152-ФЗ): только время, услуга, салон и ссылка в кабинет.
/// </para>
/// </remarks>
public sealed class DeliverPendingNotificationsHandler
    : ICommandHandler<DeliverPendingNotificationsResult, DeliverPendingNotificationsCommand>
{
    /// <summary>
    /// Причина пропуска «канал не привязан».
    /// </summary>
    private const string NOT_LINKED_REASON = "telegram is not linked";

    /// <summary>
    /// Причина пропуска «напоминания выключены».
    /// </summary>
    private const string DISABLED_REASON = "notifications are disabled";

    /// <summary>
    /// Причина пропуска «визит больше не подтверждён».
    /// </summary>
    private const string BOOKING_NOT_CONFIRMED_REASON = "booking is not confirmed";

    /// <summary>
    /// Причина пропуска «уведомление пришло без времени визита».
    /// </summary>
    private const string NO_TIME_REASON = "notification has no visit time";

    /// <summary>
    /// Причина пропуска «Telegram отклонил чат».
    /// </summary>
    private const string CHAT_UNAVAILABLE_REASON = "telegram chat is unavailable";

    /// <summary>
    /// Задержки повторов в минутах: после первой, второй и далее неудач.
    /// </summary>
    private static readonly int[] BackoffMinutes = [1, 5, 15];

    private readonly IBarberNotificationRepository _notifications;
    private readonly IBarberNotificationSettingsRepository _settings;
    private readonly IBookingRepository _bookings;
    private readonly ITelegramSender _telegram;
    private readonly RemindersOptions _remindersOptions;
    private readonly AppOptions _appOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeliverPendingNotificationsHandler> _logger;

    public DeliverPendingNotificationsHandler(
        IBarberNotificationRepository notifications,
        IBarberNotificationSettingsRepository settings,
        IBookingRepository bookings,
        ITelegramSender telegram,
        RemindersOptions remindersOptions,
        AppOptions appOptions,
        TimeProvider timeProvider,
        ILogger<DeliverPendingNotificationsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(telegram);
        ArgumentNullException.ThrowIfNull(remindersOptions);
        ArgumentNullException.ThrowIfNull(appOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _notifications = notifications;
        _settings = settings;
        _bookings = bookings;
        _telegram = telegram;
        _remindersOptions = remindersOptions;
        _appOptions = appOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Отправляет очередную пачку уведомлений и возвращает счётчики для лога воркера.
    /// </summary>
    public async Task<Result<DeliverPendingNotificationsResult, Failure>> Handle(
        DeliverPendingNotificationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _timeProvider.GetUtcNow();
        var batchSize = _remindersOptions.BatchSize > 0
            ? _remindersOptions.BatchSize
            : Constants.NOTIFICATION_BATCH_SIZE;

        var pendingResult = await _notifications.GetPendingForDeliveryAsync(
            now,
            batchSize,
            cancellationToken);
        if (pendingResult.IsFailure)
        {
            return pendingResult.Error;
        }

        var sent = 0;
        var skipped = 0;
        var failed = 0;
        var cabinetUrl = NotificationMessages.CabinetUrl(_appOptions.PublicUrl);

        // Настройки одного барбера нужны для каждого его уведомления: пачка часто состоит
        // из нескольких строк одного человека, поэтому строка настроек читается один раз.
        var settingsByUser = new Dictionary<Guid, BarberNotificationSettings?>();

        foreach (var notification in pendingResult.Value)
        {
            if (!settingsByUser.TryGetValue(notification.UserId, out var settings))
            {
                var settingsResult = await _settings.FindByUserIdAsync(
                    notification.UserId,
                    cancellationToken);
                if (settingsResult.IsFailure)
                {
                    return settingsResult.Error;
                }

                settings = settingsResult.Value;
                settingsByUser[notification.UserId] = settings;
            }

            if (notification.Kind == BarberNotificationKind.Upcoming
                && notification.BookingId is { } bookingId)
            {
                var bookingResult = await _bookings.GetByIdAsync(bookingId, cancellationToken);

                if (bookingResult.IsFailure
                    || bookingResult.Value.Status != BookingStatus.Confirmed)
                {
                    var skippedBooking = await SkipAsync(
                        notification,
                        BOOKING_NOT_CONFIRMED_REASON,
                        cancellationToken);
                    if (skippedBooking.IsFailure)
                    {
                        return skippedBooking.Error;
                    }

                    skipped++;
                    continue;
                }
            }

            if (settings is null)
            {
                var skippedMissing = await SkipAsync(notification, NOT_LINKED_REASON, cancellationToken);
                if (skippedMissing.IsFailure)
                {
                    return skippedMissing.Error;
                }

                skipped++;
                continue;
            }

            if (!settings.IsEnabled)
            {
                var skippedDisabled = await SkipAsync(notification, DISABLED_REASON, cancellationToken);
                if (skippedDisabled.IsFailure)
                {
                    return skippedDisabled.Error;
                }

                skipped++;
                continue;
            }

            if (settings.TelegramChatId is not { } chatId)
            {
                var skippedUnlinked = await SkipAsync(notification, NOT_LINKED_REASON, cancellationToken);
                if (skippedUnlinked.IsFailure)
                {
                    return skippedUnlinked.Error;
                }

                skipped++;
                continue;
            }

            var text = BuildText(notification, settings.LeadMinutes, cabinetUrl);
            if (text is null)
            {
                var skippedNoTime = await SkipAsync(notification, NO_TIME_REASON, cancellationToken);
                if (skippedNoTime.IsFailure)
                {
                    return skippedNoTime.Error;
                }

                skipped++;
                continue;
            }

            var delivery = await _telegram.SendMessageAsync(chatId, text, cancellationToken);

            if (delivery.IsSuccess)
            {
                var markedSent = notification.MarkSent();
                if (markedSent.IsFailure)
                {
                    return markedSent.Error;
                }

                var sentSaved = await _notifications.UpdateAsync(notification, cancellationToken);
                if (sentSaved.IsFailure)
                {
                    return sentSaved.Error;
                }

                sent++;
                continue;
            }

            if (IsChatUnavailable(delivery.Error))
            {
                var skippedChat = await SkipAsync(notification, CHAT_UNAVAILABLE_REASON, cancellationToken);
                if (skippedChat.IsFailure)
                {
                    return skippedChat.Error;
                }

                // Мёртвый чат отвязываем: следующий тик не должен снова пытаться в него писать.
                var unlinked = settings.UnlinkTelegram();
                if (unlinked.IsFailure)
                {
                    return unlinked.Error;
                }

                var settingsSaved = await _settings.UpdateAsync(settings, cancellationToken);
                if (settingsSaved.IsFailure)
                {
                    return settingsSaved.Error;
                }

                _logger.LogWarning(
                    "Telegram chat {ChatId} of user {UserId} is unavailable and has been unlinked.",
                    chatId,
                    notification.UserId);

                skipped++;
                continue;
            }

            // Временная ошибка: попытка записывается, повтор планируется по расписанию.
            var attempt = notification.DeliveryAttempts + 1;
            var nextAttemptAt = now.AddMinutes(Backoff(attempt));

            var markedFailed = notification.MarkFailed(
                FirstErrorMessage(delivery.Error),
                nextAttemptAt);
            if (markedFailed.IsFailure)
            {
                return markedFailed.Error;
            }

            var failedSaved = await _notifications.UpdateAsync(notification, cancellationToken);
            if (failedSaved.IsFailure)
            {
                return failedSaved.Error;
            }

            failed++;
        }

        if (sent + skipped + failed > 0)
        {
            // Итог тика логирует воркер одной строкой; здесь — подробность для Debug.
            _logger.LogDebug(
                "Reminder delivery tick: sent {Sent}, skipped {Skipped}, failed {Failed}.",
                sent,
                skipped,
                failed);
        }

        return new DeliverPendingNotificationsResult(sent, skipped, failed);
    }

    /// <summary>
    /// Переводит уведомление в <c>Skipped</c> с причиной и сохраняет его.
    /// </summary>
    private async Task<UnitResult<Failure>> SkipAsync(
        BarberNotification notification,
        string reason,
        CancellationToken cancellationToken)
    {
        var skipped = notification.MarkSkipped(reason);
        if (skipped.IsFailure)
        {
            return skipped.Error;
        }

        return await _notifications.UpdateAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Собирает текст сообщения; <c>null</c> — у уведомления нет времени, показать нечего.
    /// </summary>
    /// <remarks>
    /// Неизвестный пояс снимка не мешает отправке: время показывается в UTC — лучше, чем
    /// потерять напоминание из-за одной испорченной строки.
    /// </remarks>
    private static string? BuildText(
        BarberNotification notification,
        int leadMinutes,
        string cabinetUrl)
    {
        var timeZone = SalonTimeZone.TryResolve(notification.SalonTimeZoneId, out var resolved)
            ? resolved
            : TimeZoneInfo.Utc;

        return notification.Kind switch
        {
            BarberNotificationKind.Upcoming when notification.StartAt is { } startAt =>
                NotificationMessages.Upcoming(
                    leadMinutes,
                    SalonTimeZone.FormatTimeOfDay(startAt, timeZone),
                    notification.ServiceName,
                    notification.SalonName,
                    cabinetUrl),

            BarberNotificationKind.Digest when notification.BookingsCount is { } bookingsCount =>
                NotificationMessages.Digest(
                    bookingsCount,
                    notification.StartAt is { } firstStartAt
                        ? SalonTimeZone.FormatTimeOfDay(firstStartAt, timeZone)
                        : null,
                    cabinetUrl),

            _ => null
        };
    }

    /// <summary>
    /// Задержка следующей попытки в минутах: 1, затем 5, затем 15.
    /// </summary>
    private static int Backoff(int attempt)
    {
        var index = Math.Clamp(attempt - 1, 0, BackoffMinutes.Length - 1);

        return BackoffMinutes[index];
    }

    /// <summary>
    /// Отличает «чат недоступен» (повторять нельзя) от временной ошибки отправки.
    /// </summary>
    private static bool IsChatUnavailable(Failure failure)
        => failure.Errors.Any(error =>
            error.Code == ApiErrorCodes.Notification.TelegramChatUnavailable);

    /// <summary>
    /// Текст первой ошибки отправки — техническая диагностика для <c>LastError</c>.
    /// </summary>
    private static string? FirstErrorMessage(Failure failure)
        => failure.Errors.Count > 0 ? failure.Errors[0].Message : null;
}
