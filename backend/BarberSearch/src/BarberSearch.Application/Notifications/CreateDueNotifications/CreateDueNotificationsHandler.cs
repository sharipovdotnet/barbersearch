using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Notifications.CreateDueNotifications;

/// <summary>
/// Обработчик тика создания уведомлений — сердце напоминаний.
/// </summary>
/// <remarks>
/// <para>
/// Выполняется раз в минуту воркером. Проходит по барберам с включёнными напоминаниями и
/// создаёт два вида строк: напоминание о визите за <c>LeadMinutes</c> до начала и утренний
/// дайджест в минуту <c>DigestTime</c> локального пояса основного салона (§3.1 спецификации).
/// </para>
/// <para>
/// Окно напоминания проверяется здесь, а не только в запросе: <c>now &lt; start_at &lt;= now + lead</c>.
/// Ровно <c>now + lead</c> попадает (это последняя минута, когда напоминание ещё полезно),
/// <c>now + lead + 1 с</c> — уже нет. Так правило остаётся проверяемым на фейках, а запрос
/// в хранилище — лишь сужением выборки.
/// </para>
/// <para>
/// Дедупликацию закрывает хранилище (<c>ON CONFLICT DO NOTHING</c> по уникальным индексам),
/// поэтому повторный тик и второй экземпляр безопасны: <c>TryAddAsync</c> сообщает, была ли
/// строка вставлена, и счётчик отражает только реальные создания.
/// </para>
/// <para>
/// Неизвестный часовой пояс не роняет тик: барбер пропускается с предупреждением в лог
/// (§4 спецификации). Ошибка хранилища, наоборот, возвращается как <c>Failure</c> — воркер
/// залогирует её и продолжит со следующего тика.
/// </para>
/// </remarks>
public sealed class CreateDueNotificationsHandler
    : ICommandHandler<CreateDueNotificationsResult, CreateDueNotificationsCommand>
{
    private readonly IBarberNotificationRepository _notifications;
    private readonly IBarberNotificationSettingsRepository _settings;
    private readonly IReminderSourceRepository _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateDueNotificationsHandler> _logger;

    public CreateDueNotificationsHandler(
        IBarberNotificationRepository notifications,
        IBarberNotificationSettingsRepository settings,
        IReminderSourceRepository source,
        TimeProvider timeProvider,
        ILogger<CreateDueNotificationsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _notifications = notifications;
        _settings = settings;
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт все уведомления, которые наступили на текущей минуте.
    /// </summary>
    public async Task<Result<CreateDueNotificationsResult, Failure>> Handle(
        CreateDueNotificationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _timeProvider.GetUtcNow();

        var settingsResult = await _settings.GetEnabledAsync(cancellationToken);
        if (settingsResult.IsFailure)
        {
            return settingsResult.Error;
        }

        var created = 0;

        foreach (var settings in settingsResult.Value)
        {
            // Репозиторий уже фильтрует выключенных, но правило остаётся и здесь: выключенный
            // барбер не получает ни новых строк инбокса, ни доставки.
            if (!settings.IsEnabled)
            {
                continue;
            }

            var upcomingCreated = await CreateUpcomingAsync(settings, now, cancellationToken);
            if (upcomingCreated.IsFailure)
            {
                return upcomingCreated.Error;
            }

            created += upcomingCreated.Value;

            if (!settings.DigestEnabled)
            {
                continue;
            }

            var digestCreated = await CreateDigestAsync(settings, now, cancellationToken);
            if (digestCreated.IsFailure)
            {
                return digestCreated.Error;
            }

            created += digestCreated.Value;
        }

        if (created > 0)
        {
            // Сводку по тику пишет воркер одной строкой LogInformation; здесь — деталь
            // уровня Debug, чтобы не дублировать счётчики в журнале каждую минуту.
            _logger.LogDebug(
                "Reminder tick created {Created} notifications at {Now}.",
                created,
                now);
        }

        return new CreateDueNotificationsResult(created);
    }

    /// <summary>
    /// Создаёт напоминания о визитах барбера, попадающих в его окно <c>lead_minutes</c>.
    /// </summary>
    private async Task<Result<int, Failure>> CreateUpcomingAsync(
        BarberNotificationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Запрос берёт самый широкий допустимый интервал, а точное окно барбера проверяется ниже:
        // так правило не дублируется в SQL и остаётся проверяемым в тестах.
        var windowEnd = now.AddMinutes(Constants.MAX_NOTIFICATION_LEAD_MINUTES);

        var bookingsResult = await _source.GetUpcomingBookingsAsync(
            settings.UserId,
            now,
            windowEnd,
            cancellationToken);
        if (bookingsResult.IsFailure)
        {
            return bookingsResult.Error;
        }

        var leadEnd = now.AddMinutes(settings.LeadMinutes);
        var created = 0;

        foreach (var booking in bookingsResult.Value)
        {
            if (booking.StartAt <= now || booking.StartAt > leadEnd)
            {
                continue;
            }

            var notification = BarberNotification.Upcoming(
                settings.UserId,
                booking.BookingId,
                booking.StartAt,
                booking.ServiceName,
                booking.SalonName,
                booking.SalonTimeZoneId);

            if (notification.IsFailure)
            {
                _logger.LogWarning(
                    "Reminder for booking {BookingId} was skipped: {Code}.",
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

        return created;
    }

    /// <summary>
    /// Создаёт утренний дайджест, если локальное время барбера попало в минуту <c>DigestTime</c>
    /// и в локальных сутках салона есть подтверждённые записи.
    /// </summary>
    private async Task<Result<int, Failure>> CreateDigestAsync(
        BarberNotificationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await _source.GetPrimarySalonTimeZoneIdAsync(
            settings.UserId,
            cancellationToken);
        if (timeZoneId.IsFailure)
        {
            return timeZoneId.Error;
        }

        if (string.IsNullOrWhiteSpace(timeZoneId.Value))
        {
            // Нет основного салона — нет пояса, локальные сутки посчитать не от чего.
            return 0;
        }

        if (!SalonTimeZone.TryResolve(timeZoneId.Value, out var timeZone))
        {
            _logger.LogWarning(
                "Digest for user {UserId} was skipped: time zone '{TimeZoneId}' is unknown.",
                settings.UserId,
                timeZoneId.Value);
            return 0;
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localMinute = TimeOnly.FromDateTime(localNow.DateTime);
        var windowEnd = settings.DigestTime.AddMinutes(Constants.NOTIFICATION_DIGEST_WINDOW_MINUTES);

        if (localMinute < settings.DigestTime || localMinute >= windowEnd)
        {
            return 0;
        }

        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var dayStart = SalonTimeZone.ToUtc(
            localDate.ToDateTime(TimeOnly.MinValue),
            timeZone);
        var dayEnd = SalonTimeZone.ToUtc(
            localDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            timeZone);

        if (dayStart is null || dayEnd is null)
        {
            _logger.LogWarning(
                "Digest for user {UserId} was skipped: the salon local day has no UTC bounds.",
                settings.UserId);
            return 0;
        }

        var bookingsResult = await _source.GetDayBookingsAsync(
            settings.UserId,
            dayStart.Value,
            dayEnd.Value,
            cancellationToken);
        if (bookingsResult.IsFailure)
        {
            return bookingsResult.Error;
        }

        var bookings = bookingsResult.Value;

        if (bookings.Count == 0)
        {
            // Дайджест «сегодня 0 записей» — шум, а не напоминание.
            return 0;
        }

        var first = bookings[0];

        var notification = BarberNotification.Digest(
            settings.UserId,
            bookings.Count,
            localDate,
            first.StartAt,
            first.ServiceName,
            first.SalonName,
            timeZoneId.Value);

        if (notification.IsFailure)
        {
            _logger.LogWarning(
                "Digest for user {UserId} was skipped: {Code}.",
                settings.UserId,
                notification.Error.Errors[0].Code);
            return 0;
        }

        var added = await _notifications.TryAddAsync(notification.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        return added.Value ? 1 : 0;
    }
}
