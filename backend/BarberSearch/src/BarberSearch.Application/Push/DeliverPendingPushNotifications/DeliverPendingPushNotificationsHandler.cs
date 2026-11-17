using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Push.DeliverPendingPushNotifications;

// Alias, а не using на пространство имён: рядом живёт BarberSearch.Application.Push.BookingStatus,
// и без псевдонима имя BookingStatus разрешалось бы в него, а не в доменный enum.
using BookingStatus = BarberSearch.Domain.Bookings.BookingStatus;

/// <summary>
/// Обработчик тика доставки: отправляет пачку <c>Pending</c>-уведомлений на устройства.
/// </summary>
/// <remarks>
/// <para>
/// Сначала проверяется актуальность повода, и правило зависит от вида уведомления. Для
/// напоминания: визит отменили, завершили, отметили неявкой или перенесли — <c>Skipped</c>
/// с причиной <c>booking is not confirmed</c>; начало уже наступило — <c>window closed</c>.
/// Для события создания брони: бронь исчезла — <c>booking was deleted</c>, отменена —
/// <c>booking is cancelled</c>, перенесена — <c>booking was rescheduled</c>, начало
/// наступило — <c>window closed</c>. <c>Pending</c>-самозапись клиента — ожидаемое
/// состояние события создания, она доставляется.
/// </para>
/// <para>
/// События статуса проверяются по своему правилу. Подтверждение доставляется только пока
/// бронь <c>Confirmed</c> (иначе — та же причина <c>booking is not confirmed</c>). Отмена —
/// только пока бронь <c>Cancelled</c> (<c>booking is not cancelled</c>: запись «ожила» после
/// отмены, и устаревшее сообщение об отмене было бы ложью). Это правило общее для отмены
/// барбером и клиентом: вид разный, повод один. Перенос — только пока визит
/// активен, то есть не <c>Cancelled</c>/<c>Completed</c>/<c>NoShow</c>
/// (<c>booking is not active</c>); правило тоже общее для двух видов переноса. Общие для
/// всех события статуса проверки: исчезнувшая
/// бронь — <c>booking was deleted</c>, несовпадение <c>StartAt</c> со снимком —
/// <c>booking was rescheduled</c> (новый перенос отменяет старый), наступившее начало —
/// <c>window closed</c>.
/// </para>
/// <para>
/// Одно уведомление — все устройства пользователя: FCM HTTP v1 не умеет батч, поэтому
/// на каждый токен уходит отдельный запрос. Успех хотя бы одного устройства — <c>Sent</c>.
/// Токен, который FCM отверг как <c>UNREGISTERED</c>/<c>INVALID_ARGUMENT</c>, удаляется и
/// не участвует в следующих тиках; если мёртвыми оказались все, уведомление получает
/// <c>Skipped</c> с причиной «нет устройства».
/// </para>
/// <para>
/// Временная ошибка (<c>429</c>, <c>5xx</c>, сеть, <c>401/403</c>) — <c>MarkFailed</c>
/// и повтор по расписанию 1/5/15 минут; на пределе попыток домен ставит <c>Failed</c>.
/// </para>
/// <para>
/// Режим разработки (<c>PushOptions.DevMode</c>, только Development): <c>Push:Enabled=true</c>
/// без ключей. Текст собирается и один раз пишется в журнал (без токена), строка становится
/// <c>Skipped</c> с причиной <c>push is not configured</c> — уведомление не должно
/// зависать в <c>Pending</c>. Строка журнала называет вид уведомления, а не «reminder»
/// для любого повода. В остальных окружениях ненастроенный канал оставляет строки
/// <c>Pending</c> (они уйдут после починки конфигурации) и пишет одно предупреждение.
/// В рабочем режиме ни токен, ни полный текст в журнал не попадают.
/// </para>
/// </remarks>
public sealed class DeliverPendingPushNotificationsHandler
    : ICommandHandler<DeliverPendingPushNotificationsResult, DeliverPendingPushNotificationsCommand>
{
    /// <summary>
    /// Причина пропуска «визит больше не подтверждён» — только для напоминания.
    /// </summary>
    private const string BOOKING_NOT_CONFIRMED_REASON = "booking is not confirmed";

    /// <summary>
    /// Причина пропуска «событие создания относится к удалённой брони».
    /// </summary>
    private const string BOOKING_DELETED_REASON = "booking was deleted";

    /// <summary>
    /// Причина пропуска «бронь отменена после создания события».
    /// </summary>
    private const string BOOKING_CANCELLED_REASON = "booking is cancelled";

    /// <summary>
    /// Причина пропуска «бронь больше не отменена» — только для события отмены.
    /// </summary>
    private const string BOOKING_NOT_CANCELLED_REASON = "booking is not cancelled";

    /// <summary>
    /// Причина пропуска «визит больше не активен» — для события переноса.
    /// </summary>
    private const string BOOKING_NOT_ACTIVE_REASON = "booking is not active";

    /// <summary>
    /// Причина пропуска «визит перенесли после создания уведомления».
    /// </summary>
    private const string RESCHEDULED_REASON = "booking was rescheduled";

    /// <summary>
    /// Причина пропуска «время визита уже наступило».
    /// </summary>
    private const string WINDOW_CLOSED_REASON = "window closed";

    /// <summary>
    /// Причина пропуска «у пользователя нет зарегистрированных устройств».
    /// </summary>
    private const string NO_DEVICE_REASON = "no registered device";

    /// <summary>
    /// Причина пропуска «push не настроен на сервере» — режим разработки.
    /// </summary>
    private const string NOT_CONFIGURED_REASON = "push is not configured";

    /// <summary>
    /// Задержки повторов в минутах: после первой, второй и далее неудач.
    /// </summary>
    private static readonly int[] BackoffMinutes = [1, 5, 15];

    /// <summary>
    /// Признак «предупреждение о ненастроенном канале уже написано»; см. <see cref="LogUnconfiguredOnce"/>.
    /// </summary>
    private static int _unconfiguredWarningLogged;

    private readonly IPushNotificationRepository _notifications;
    private readonly IPushDeviceTokenRepository _tokens;
    private readonly IBookingRepository _bookings;
    private readonly IPushSender _pushSender;
    private readonly PushOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeliverPendingPushNotificationsHandler> _logger;

    public DeliverPendingPushNotificationsHandler(
        IPushNotificationRepository notifications,
        IPushDeviceTokenRepository tokens,
        IBookingRepository bookings,
        IPushSender pushSender,
        PushOptions options,
        TimeProvider timeProvider,
        ILogger<DeliverPendingPushNotificationsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(pushSender);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _notifications = notifications;
        _tokens = tokens;
        _bookings = bookings;
        _pushSender = pushSender;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Отправляет очередную пачку уведомлений и возвращает счётчики для лога воркера.
    /// </summary>
    public async Task<Result<DeliverPendingPushNotificationsResult, Failure>> Handle(
        DeliverPendingPushNotificationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _timeProvider.GetUtcNow();

        var batchSize = _options.BatchSize > 0
            ? _options.BatchSize
            : PushOptions.DEFAULT_BATCH_SIZE;

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

        foreach (var notification in pendingResult.Value)
        {
            var skipReason = await ResolveSkipReasonAsync(notification, now, cancellationToken);
            if (skipReason.IsFailure)
            {
                return skipReason.Error;
            }

            if (skipReason.Value is { } reason)
            {
                var markedSkipped = await SkipAsync(notification, reason, cancellationToken);
                if (markedSkipped.IsFailure)
                {
                    return markedSkipped.Error;
                }

                skipped++;
                continue;
            }

            var message = PushMessages.For(notification, now);

            if (!_pushSender.IsConfigured)
            {
                if (_options.DevMode)
                {
                    // Режим разработки: показываем текст, чтобы его можно было проверить глазами,
                    // но не отправляем и не оставляем строку в Pending навсегда. Вид уведомления
                    // называется явно: «reminder» для события создания был бы ложью.
                    _logger.LogInformation(
                        "Push is not configured: booking {BookingId} {NotificationKind} would be «{Title}» / «{Body}».",
                        notification.BookingId,
                        KindLabel(notification.Kind),
                        message.Title,
                        message.Body);

                    var markedUnconfigured = await SkipAsync(
                        notification,
                        NOT_CONFIGURED_REASON,
                        cancellationToken);
                    if (markedUnconfigured.IsFailure)
                    {
                        return markedUnconfigured.Error;
                    }

                    skipped++;
                    continue;
                }

                // Бой без ключей — ошибка эксплуатации, а не повод потерять уведомление:
                // строку не трогаем (она останется Pending и уйдёт после починки конфигурации),
                // предупреждение пишем один раз, а не на каждую строку каждого тика.
                LogUnconfiguredOnce();

                continue;
            }

            var devicesResult = await _tokens.GetByUserIdAsync(notification.UserId, cancellationToken);
            if (devicesResult.IsFailure)
            {
                return devicesResult.Error;
            }

            // iOS зарезервирован под будущий APNs-слайс: токен храним, но через FCM не шлём —
            // FCM его отвергнет как INVALID_ARGUMENT, и строка была бы удалена как мёртвая.
            var devices = devicesResult.Value
                .Where(device => device.Platform == PushPlatform.Android)
                .ToList();

            if (devices.Count == 0)
            {
                var markedNoDevice = await SkipAsync(notification, NO_DEVICE_REASON, cancellationToken);
                if (markedNoDevice.IsFailure)
                {
                    return markedNoDevice.Error;
                }

                skipped++;
                continue;
            }

            var delivered = false;
            var transportFailure = false;
            Failure? transportError = null;

            foreach (var device in devices)
            {
                var delivery = await _pushSender.SendAsync(message, device.Token, cancellationToken);

                if (delivery.IsSuccess)
                {
                    delivered = true;
                    continue;
                }

                if (IsTokenUnregistered(delivery.Error))
                {
                    // Токен мёртв: удаляем строку, чтобы следующие тики не тратили на него запрос.
                    var removed = await _tokens.DeleteByTokenAsync(device.Token, cancellationToken);
                    if (removed.IsFailure)
                    {
                        return removed.Error;
                    }

                    continue;
                }

                // Любая другая ошибка — ошибка транспорта: повторять остальные токены в этом
                // же тике бессмысленно, они, скорее всего, упадут так же.
                transportFailure = true;
                transportError = delivery.Error;
                break;
            }

            if (delivered)
            {
                var markedSent = notification.MarkSent();
                if (markedSent.IsFailure)
                {
                    return markedSent.Error;
                }

                var saved = await _notifications.UpdateAsync(notification, cancellationToken);
                if (saved.IsFailure)
                {
                    return saved.Error;
                }

                sent++;
                continue;
            }

            if (transportFailure && transportError is not null)
            {
                var attempt = notification.DeliveryAttempts + 1;
                var nextAttemptAt = now.AddMinutes(Backoff(attempt));

                var markedFailed = notification.MarkFailed(
                    FirstErrorCode(transportError),
                    nextAttemptAt,
                    _options.MaxAttempts);
                if (markedFailed.IsFailure)
                {
                    return markedFailed.Error;
                }

                var saved = await _notifications.UpdateAsync(notification, cancellationToken);
                if (saved.IsFailure)
                {
                    return saved.Error;
                }

                _logger.LogWarning(
                    "Push delivery for booking {BookingId} failed; the next tick will retry.",
                    notification.BookingId);

                failed++;
                continue;
            }

            // Сюда попадаем, когда все токены оказались мёртвыми: устройства были, но ни одно
            // не приняло сообщение — для пользователя это тот же «нет устройства».
            var markedGone = await SkipAsync(notification, NO_DEVICE_REASON, cancellationToken);
            if (markedGone.IsFailure)
            {
                return markedGone.Error;
            }

            skipped++;
        }

        if (sent + skipped + failed > 0)
        {
            // Итог тика логирует воркер одной строкой; здесь — подробность для Debug.
            _logger.LogDebug(
                "Push delivery tick: sent {Sent}, skipped {Skipped}, failed {Failed}.",
                sent,
                skipped,
                failed);
        }

        return new DeliverPendingPushNotificationsResult(sent, skipped, failed);
    }

    /// <summary>
    /// Проверяет, нужна ли ещё доставка; возвращает причину пропуска или <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Вид уведомления решает, какие проверки применимы. Напоминание существует только для
    /// подтверждённого визита: любой другой статус (в том числе удалённая бронь) — одна
    /// причина <c>booking is not confirmed</c>, потому что для клиента такой записи больше нет.
    /// </para>
    /// <para>
    /// Событие создания доставляется и для <c>Pending</c>-самозаписи клиента: подтверждение
    /// не требуется. Отмена и удаление — разные причины: отменённая бронь ещё существует,
    /// удалённая — уже нет.
    /// </para>
    /// <para>
    /// События статуса проверяют свой повод, а не статус «вообще». Подтверждение требует
    /// <c>Confirmed</c>: отменённая, завершённая или неявившаяся бронь делает сообщение
    /// ложью. Отмена требует <c>Cancelled</c>: если запись снова активна, устаревшее
    /// сообщение об отмене только запутает. Перенос требует активного визита
    /// (<c>Pending</c> или <c>Confirmed</c>): терминальный визит двигать уже нечего.
    /// Перенос и закрытое окно общие для всех видов.
    /// </para>
    /// <para>
    /// Неизвестный вид — ошибка программы, а не повод доставить сообщение «на всякий
    /// случай»: <c>Enum.IsDefined</c> в домене и check-ограничение в базе такого не
    /// допускают, поэтому здесь исключение.
    /// </para>
    /// </remarks>
    private async Task<Result<string?, Failure>> ResolveSkipReasonAsync(
        PushNotification notification,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var bookingResult = await _bookings.GetByIdAsync(notification.BookingId, cancellationToken);

        switch (notification.Kind)
        {
            case PushNotificationKind.Reminder:
                if (bookingResult.IsFailure
                    || bookingResult.Value.Status != BookingStatus.Confirmed)
                {
                    return Result.Success<string?, Failure>(BOOKING_NOT_CONFIRMED_REASON);
                }

                break;

            case PushNotificationKind.BookingCreatedByCustomer:
            case PushNotificationKind.BookingCreatedByBarber:
                if (bookingResult.IsFailure)
                {
                    return Result.Success<string?, Failure>(BOOKING_DELETED_REASON);
                }

                if (bookingResult.Value.Status == BookingStatus.Cancelled)
                {
                    return Result.Success<string?, Failure>(BOOKING_CANCELLED_REASON);
                }

                break;

            case PushNotificationKind.BookingConfirmed:
                if (bookingResult.IsFailure)
                {
                    return Result.Success<string?, Failure>(BOOKING_DELETED_REASON);
                }

                if (bookingResult.Value.Status != BookingStatus.Confirmed)
                {
                    return Result.Success<string?, Failure>(BOOKING_NOT_CONFIRMED_REASON);
                }

                break;

            case PushNotificationKind.BookingCancelledByBarber:
            case PushNotificationKind.BookingCancelledByCustomer:
                if (bookingResult.IsFailure)
                {
                    return Result.Success<string?, Failure>(BOOKING_DELETED_REASON);
                }

                if (bookingResult.Value.Status != BookingStatus.Cancelled)
                {
                    return Result.Success<string?, Failure>(BOOKING_NOT_CANCELLED_REASON);
                }

                break;

            case PushNotificationKind.BookingRescheduledByBarber:
            case PushNotificationKind.BookingRescheduledByCustomer:
                if (bookingResult.IsFailure)
                {
                    return Result.Success<string?, Failure>(BOOKING_DELETED_REASON);
                }

                if (bookingResult.Value.Status
                    is BookingStatus.Cancelled
                    or BookingStatus.Completed
                    or BookingStatus.NoShow)
                {
                    return Result.Success<string?, Failure>(BOOKING_NOT_ACTIVE_REASON);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(notification),
                    notification.Kind,
                    "Unsupported push notification kind.");
        }

        // Перенос и закрытое окно общие для всех видов: снимок StartAt — это повод, а не
        // текущее состояние визита. Для переноса он же отсекает устаревшее событие, когда
        // визит уже перенесли ещё раз.
        if (bookingResult.Value.StartAt != notification.StartAt)
        {
            return Result.Success<string?, Failure>(RESCHEDULED_REASON);
        }

        if (now >= notification.StartAt)
        {
            return Result.Success<string?, Failure>(WINDOW_CLOSED_REASON);
        }

        return Result.Success<string?, Failure>(null);
    }

    /// <summary>
    /// Человекочитаемое название вида уведомления для журнала режима разработки.
    /// </summary>
    private static string KindLabel(PushNotificationKind kind)
        => kind switch
        {
            PushNotificationKind.Reminder => "reminder",
            PushNotificationKind.BookingCreatedByCustomer
                or PushNotificationKind.BookingCreatedByBarber => "creation event",
            PushNotificationKind.BookingConfirmed => "confirmation event",
            PushNotificationKind.BookingCancelledByBarber
                or PushNotificationKind.BookingCancelledByCustomer => "cancellation event",
            PushNotificationKind.BookingRescheduledByBarber
                or PushNotificationKind.BookingRescheduledByCustomer => "reschedule event",
            _ => "unknown event"
        };

    /// <summary>
    /// Переводит уведомление в <c>Skipped</c> с причиной и сохраняет его.
    /// </summary>
    private async Task<UnitResult<Failure>> SkipAsync(
        PushNotification notification,
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
    /// Задержка следующей попытки в минутах: 1, затем 5, затем 15.
    /// </summary>
    private static int Backoff(int attempt)
    {
        var index = Math.Clamp(attempt - 1, 0, BackoffMinutes.Length - 1);

        return BackoffMinutes[index];
    }

    /// <summary>
    /// Отличает мёртвый токен (повторять нельзя) от временной ошибки транспорта.
    /// </summary>
    private static bool IsTokenUnregistered(Failure failure)
        => failure.Errors.Any(error =>
            error.Code == ApiErrorCodes.Push.TokenUnregistered);

    /// <summary>
    /// Код первой ошибки отправки — стабильная диагностика для <c>LastError</c>, а не текст
    /// внешнего сервиса.
    /// </summary>
    private static string? FirstErrorCode(Failure failure)
        => failure.Errors.Count > 0 ? failure.Errors[0].Code : null;

    /// <summary>
    /// Пишет предупреждение о включённом, но ненастроенном канале один раз на процесс.
    /// </summary>
    /// <remarks>
    /// Флаг статический, потому что обработчик scoped и пересоздаётся каждый тик; иначе
    /// журнал получал бы строку на каждое напоминание каждую минуту.
    /// </remarks>
    private void LogUnconfiguredOnce()
    {
        if (Interlocked.Exchange(ref _unconfiguredWarningLogged, 1) == 0)
        {
            _logger.LogWarning(
                "Push is enabled but not configured: notifications stay pending until credentials are set.");
        }
    }
}
