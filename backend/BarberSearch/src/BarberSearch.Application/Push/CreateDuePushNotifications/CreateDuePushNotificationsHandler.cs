using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Push.CreateDuePushNotifications;

/// <summary>
/// Обработчик тика создания push-напоминаний: за час до подтверждённого визита.
/// </summary>
/// <remarks>
/// <para>
/// Окно создания — <c>now &lt; start_at &lt;= now + ReminderLeadMinutes</c>. Ровно
/// <c>now + lead</c> попадает (это последняя минута, когда напоминание ещё полезно),
/// <c>now + lead + 1 с</c> — уже нет. Запрос в хранилище — лишь сужение выборки, поэтому
/// правило проверяется и здесь и остаётся тестируемым на фейках.
/// </para>
/// <para>
/// Бронь, созданная внутри окна, не получает «через час»: если запись на 13:50 сделана
/// в 13:40, push в 13:40 бессмысленен и только раздражает. Условие
/// <c>created_at &lt;= start_at − lead</c> есть и в SQL, и здесь — как последняя защита
/// от строки, пришедшей из источника при другом значении окна.
/// </para>
/// <para>
/// После простоя тик может впервые увидеть визит за десять минут до начала: окно ещё
/// формально открыто, но текст «Через час» — ложь. Такие строки пропускаются, если до
/// начала осталось меньше окна больше чем на два тика (и не больше пяти минут): обычный
/// тик в 60 секунд успевает, а «проснулись поздно» — нет.
/// </para>
/// <para>
/// Дедупликацию закрывает уникальный индекс по паре <c>(booking_id, kind)</c> и
/// <c>INSERT … ON CONFLICT DO NOTHING</c>: повторный тик и второй экземпляр безопасны,
/// а <c>TryAddAsync</c> сообщает, была ли строка вставлена. Событие создания брони имеет
/// другой <c>kind</c> и напоминанию не мешает.
/// </para>
/// </remarks>
public sealed class CreateDuePushNotificationsHandler
    : ICommandHandler<CreateDuePushNotificationsResult, CreateDuePushNotificationsCommand>
{
    /// <summary>
    /// Верхняя граница допуска на опоздание тика, секунды. Пять минут покрывают паузу
    /// процесса и медленный первый тик, но не превращают «через час» в «через десять минут».
    /// </summary>
    private const int MAX_LATE_TOLERANCE_SECONDS = 300;

    private readonly IPushNotificationRepository _notifications;
    private readonly IPushSourceRepository _source;
    private readonly PushOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateDuePushNotificationsHandler> _logger;

    public CreateDuePushNotificationsHandler(
        IPushNotificationRepository notifications,
        IPushSourceRepository source,
        PushOptions options,
        TimeProvider timeProvider,
        ILogger<CreateDuePushNotificationsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _notifications = notifications;
        _source = source;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт напоминания для визитов, попавших в окно на текущем тике.
    /// </summary>
    public async Task<Result<CreateDuePushNotificationsResult, Failure>> Handle(
        CreateDuePushNotificationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _timeProvider.GetUtcNow();

        var leadMinutes = _options.ReminderLeadMinutes > 0
            ? _options.ReminderLeadMinutes
            : PushOptions.DEFAULT_REMINDER_LEAD_MINUTES;

        var intervalSeconds = _options.IntervalSeconds > 0
            ? _options.IntervalSeconds
            : PushOptions.DEFAULT_INTERVAL_SECONDS;

        var windowEnd = now.AddMinutes(leadMinutes);

        // После простоя тик может впервые увидеть визит, до которого осталось десять минут:
        // окно wide enough, но текст «Через час» уже ложь. Допускаем опоздание не больше
        // двух тиков (и не больше пяти минут): обычный тик в 60 секунд укладывается,
        // а «проснулись за десять минут до визита» — нет.
        var toleranceSeconds = Math.Clamp(
            intervalSeconds * 2,
            1,
            MAX_LATE_TOLERANCE_SECONDS);
        var latestAllowedRemaining = TimeSpan.FromMinutes(leadMinutes)
            - TimeSpan.FromSeconds(toleranceSeconds);

        var bookingsResult = await _source.GetDueBookingsAsync(
            now,
            windowEnd,
            leadMinutes,
            cancellationToken);
        if (bookingsResult.IsFailure)
        {
            return bookingsResult.Error;
        }

        var created = 0;
        var skippedLate = 0;

        foreach (var booking in bookingsResult.Value)
        {
            if (booking.StartAt <= now || booking.StartAt > windowEnd)
            {
                continue;
            }

            // Бронь, созданная внутри окна напоминания, — не «через час», а только что
            // оформленная запись; push о ней не создаётся (см. примечание класса).
            if (booking.BookingCreatedAt > booking.StartAt.AddMinutes(-leadMinutes))
            {
                continue;
            }

            if (booking.StartAt - now < latestAllowedRemaining)
            {
                skippedLate++;

                _logger.LogDebug(
                    "Push reminder for booking {BookingId} was skipped as late: {Remaining} left.",
                    booking.BookingId,
                    booking.StartAt - now);
                continue;
            }

            var notification = PushNotification.Create(
                PushNotificationKind.Reminder,
                booking.UserId,
                booking.BookingId,
                booking.StartAt,
                booking.SalonName,
                booking.BarberName,
                booking.ServiceName,
                booking.SalonTimeZoneId);
            if (notification.IsFailure)
            {
                _logger.LogWarning(
                    "Push reminder for booking {BookingId} was skipped: {Code}.",
                    booking.BookingId,
                    notification.Error.Errors[0].Code);
                continue;
            }

            var added = await _notifications.TryAddAsync(notification.Value, cancellationToken);
            if (added.IsFailure)
            {
                return added.Error;
            }

            if (added.Value)
            {
                created++;
            }
        }

        if (created > 0 || skippedLate > 0)
        {
            // Сводку по тику пишет воркер одной строкой; здесь — подробность для Debug.
            _logger.LogDebug(
                "Push tick created {Created} notifications and skipped {SkippedLate} as late at {Now}.",
                created,
                skippedLate,
                now);
        }

        return new CreateDuePushNotificationsResult(created, skippedLate);
    }
}
