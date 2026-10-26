using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.GetBarberSlots;

/// <summary>
/// Обработчик запроса свободных слотов.
/// </summary>
/// <remarks>
/// Границы дня и рабочего интервала считаются в часовом поясе салона
/// (<c>Salon.TimeZoneId</c>) и только затем переводятся в UTC — неявного UTC-сдвига нет.
/// Сетка строится по локальному времени салона, поэтому переход на летнее время
/// не сдвигает начало рабочего дня. Слот исключается, если пересекается с отпуском
/// или активной бронью; отменённые и завершённые брони слот не занимают.
/// Если услуга задана, в ответ попадают <c>barberServiceId</c> и <c>hairServiceId</c>
/// той пары, по которой посчитана длительность: именно <c>barberServiceId</c> нужен
/// для создания брони (R-20).
/// </remarks>
public sealed class GetBarberSlotsHandler
    : IQueryHandler<BarberSlotsResponse, GetBarberSlotsQuery>
{
    private readonly IBarberProfileRepository _profileRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetBarberSlotsHandler> _logger;

    public GetBarberSlotsHandler(
        IBarberProfileRepository profileRepository,
        IBookingRepository bookingRepository,
        TimeProvider timeProvider,
        ILogger<GetBarberSlotsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(profileRepository);
        ArgumentNullException.ThrowIfNull(bookingRepository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _profileRepository = profileRepository;
        _bookingRepository = bookingRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает свободные слоты или 404, если профиль/салон/услуга не найдены.
    /// </summary>
    public async Task<Result<BarberSlotsResponse, Failure>> Handle(
        GetBarberSlotsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.BarberProfileId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                "barberSlots.barberProfileId",
                "Barber profile identifier cannot be empty.",
                nameof(query.BarberProfileId)));
        }

        if (query.Date is not { } date)
        {
            return new Failure(Error.Validation(
                "barberSlots.date",
                "The date is required.",
                nameof(query.Date)));
        }

        var salonId = query.SalonId == Guid.Empty ? null : query.SalonId;
        var hairServiceId = query.HairServiceId == Guid.Empty ? null : query.HairServiceId;

        var contextResult = await _profileRepository.GetSlotContextAsync(
            query.BarberProfileId,
            salonId,
            hairServiceId,
            date,
            cancellationToken);
        if (contextResult.IsFailure)
        {
            return contextResult.Error;
        }

        var context = contextResult.Value;

        if (context.DurationMinutes <= 0)
        {
            return new Failure(Error.Failure(
                "barberSlots.duration.invalid",
                "The slot duration must be positive."));
        }

        // Пояс проверяется при записи салона (Salon.Create / UpdateLocation), поэтому сюда
        // попадает только историческая или повреждённая запись. Это не ошибка клиента,
        // но и не 500: отвечаем 409, а в лог пишем алерт с идентификаторами, чтобы данные
        // можно было найти и починить.
        if (string.IsNullOrWhiteSpace(context.TimeZoneId))
        {
            _logger.LogError(
                "Salon {SalonId} of barber profile {BarberProfileId} has an empty time zone; slots cannot be calculated.",
                context.SalonId,
                context.BarberProfileId);

            return new Failure(Error.Conflict(
                "barberSlots.timeZone.missing",
                $"Salon '{context.SalonId}' has no time zone configured."));
        }

        if (!TryResolveTimeZone(context.TimeZoneId, out var timeZone))
        {
            _logger.LogError(
                "Salon {SalonId} of barber profile {BarberProfileId} has an unresolvable time zone '{TimeZoneId}'; slots cannot be calculated.",
                context.SalonId,
                context.BarberProfileId,
                context.TimeZoneId);

            return new Failure(Error.Conflict(
                "barberSlots.timeZone.unknown",
                $"Salon time zone '{context.TimeZoneId}' cannot be resolved."));
        }

        var dayStart = ToUtc(date.ToDateTime(TimeOnly.MinValue), timeZone);
        var dayEnd = ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);

        if (dayStart is null || dayEnd is null)
        {
            return new Failure(Error.Failure(
                "barberSlots.day.invalid",
                $"Local day boundaries for '{date}' cannot be converted to UTC in time zone '{context.TimeZoneId}'."));
        }

        var response = new BarberSlotsResponse(
            context.BarberProfileId,
            context.SalonId,
            context.BarberServiceId,
            context.HairServiceId,
            date,
            context.TimeZoneId,
            dayStart.Value,
            dayEnd.Value,
            context.DurationMinutes,
            context.IsWorkingDay,
            []);

        if (!context.IsWorkingDay)
        {
            return response;
        }

        var bookingsResult = await _bookingRepository.GetActiveByBarberAsync(
            context.BarberProfileId,
            dayStart.Value,
            dayEnd.Value,
            cancellationToken);
        if (bookingsResult.IsFailure)
        {
            return bookingsResult.Error;
        }

        var workStartLocal = date.ToDateTime(context.StartTime);
        var workEndLocal = date.ToDateTime(context.EndTime);
        var now = _timeProvider.GetUtcNow();
        var slots = new List<BarberSlotResponse>();
        var seen = new HashSet<DateTimeOffset>();

        for (var localStart = workStartLocal;
             localStart.AddMinutes(context.DurationMinutes) <= workEndLocal;
             localStart = localStart.AddMinutes(context.DurationMinutes))
        {
            var localEnd = localStart.AddMinutes(context.DurationMinutes);
            var slotStart = ToUtc(localStart, timeZone);
            var slotEnd = ToUtc(localEnd, timeZone);

            if (slotStart is null || slotEnd is null)
            {
                // Несуществующее локальное время (переход на летнее время) пропускается.
                continue;
            }

            // При переходе на зимнее время два локальных времени могут дать один UTC-инстант.
            if (!seen.Add(slotStart.Value))
            {
                continue;
            }

            if (slotStart.Value <= now)
            {
                continue;
            }

            if (context.TimeOffs.Any(timeOff =>
                    timeOff.StartAt < slotEnd.Value && slotStart.Value < timeOff.EndAt))
            {
                continue;
            }

            if (bookingsResult.Value.Any(booking =>
                    booking.StartAt < slotEnd.Value && slotStart.Value < booking.EndAt))
            {
                continue;
            }

            slots.Add(new BarberSlotResponse(slotStart.Value, slotEnd.Value));
        }

        return response with { Slots = slots };
    }

    /// <summary>
    /// Переводит локальное время салона в UTC. Несуществующее время (дыра перехода
    /// на летнее время) возвращает <c>null</c>.
    /// </summary>
    private static DateTimeOffset? ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(unspecified))
        {
            return null;
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);

        return new DateTimeOffset(utc);
    }

    /// <summary>
    /// Разрешает часовой пояс салона: сначала как системный идентификатор (IANA на Linux,
    /// IANA/Windows на Windows), затем — исторический Windows-идентификатор через
    /// конвертацию в IANA. Неразрешимый пояс возвращает <c>false</c>, а не исключение.
    /// </summary>
    private static bool TryResolveTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        var candidate = timeZoneId.Trim();

        if (TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out var found))
        {
            timeZone = found!;
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out found))
        {
            timeZone = found!;
            return true;
        }

        timeZone = null!;
        return false;
    }
}
