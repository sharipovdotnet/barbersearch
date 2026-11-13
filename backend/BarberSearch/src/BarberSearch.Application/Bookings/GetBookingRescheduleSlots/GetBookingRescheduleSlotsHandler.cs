using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.GetBookingRescheduleSlots;

/// <summary>
/// Обработчик чтения слотов для переноса визита.
/// </summary>
/// <remarks>
/// <para>
/// Контракт — существующий <see cref="BarberSlotsResponse"/> (D7): идентификаторы и длительность
/// берутся из самой брони, а не из прайса (D6). Поэтому такой визит нельзя пересчитать публичным
/// <c>/slots</c>, который шагает по текущей длительности услуги и требует видимый профиль.
/// </para>
/// <para>
/// Порядок шагов обязателен: пояс салона нужен, чтобы понять «сегодня», но появляется он только
/// вместе с контекстом дня, а контекст требует дату. Поэтому дата проверяется на существование,
/// контекст читается на запрошенную дату и лишь затем — границы окна
/// <c>[сегодня; +90 дней]</c>. Запрос по «далёкой» дате безвреден и отсекается до расчёта сетки.
/// </para>
/// <para>
/// Своя бронь не занимает свой слот: перенос на то же время разрешён (D15), поэтому занятость
/// фильтруется с исключением самой брони. Остальные правила сетки — <see cref="SlotGrid"/>.
/// </para>
/// </remarks>
public sealed class GetBookingRescheduleSlotsHandler
    : IQueryHandler<BarberSlotsResponse, GetBookingRescheduleSlotsQuery>
{
    private readonly IBookingRepository _bookings;
    private readonly IBarberProfileRepository _profiles;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetBookingRescheduleSlotsHandler> _logger;

    public GetBookingRescheduleSlotsHandler(
        IBookingRepository bookings,
        IBarberProfileRepository profiles,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<GetBookingRescheduleSlotsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _bookings = bookings;
        _profiles = profiles;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает легальные слоты дня для переноса визита.
    /// </summary>
    public async Task<Result<BarberSlotsResponse, Failure>> Handle(
        GetBookingRescheduleSlotsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var bookingResult = await _bookings.GetByIdAsync(query.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;
        var isClientOwner = booking.UserId is { } owner && owner == userIdResult.Value;

        // Гостевую запись читает барбер профиля или администратор: владельца-клиента у неё нет.
        if (!isClientOwner)
        {
            var allowed = await _currentUser.EnsureIsBarberOfAsync(
                _profiles,
                booking.BarberProfileId,
                cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        var now = _timeProvider.GetUtcNow();

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleStatus,
                $"Booking in status {booking.Status} cannot be rescheduled."));
        }

        // Начавшийся визит не переносится: его двигают завершением или неявкой. Для чтения
        // сетки ответ тот же — «перенести уже нельзя», и он честнее пустого списка слотов.
        if (booking.StartAt <= now)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleStatus,
                "The booking has already started."));
        }

        if (query.Date is not { } date)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.RescheduleDate,
                "The date is required.",
                nameof(query.Date)));
        }

        // Длительность — из брони, поэтому услуга справочника в контекст не передаётся (D6).
        // Видимость разная: клиент-владелец видит только публичный профиль, барбер и админ —
        // свой, даже если профиль ещё не опубликован.
        var contextResult = isClientOwner
            ? await _profiles.GetSlotContextAsync(
                booking.BarberProfileId,
                booking.SalonId,
                hairServiceId: null,
                date,
                cancellationToken)
            : await _profiles.GetOwnSlotContextAsync(
                booking.BarberProfileId,
                booking.SalonId,
                hairServiceId: null,
                date,
                cancellationToken);
        if (contextResult.IsFailure)
        {
            return contextResult.Error;
        }

        var context = contextResult.Value;

        // Историческая или повреждённая запись: день в таком поясе не считается. Это не ошибка
        // клиента, поэтому 409 с общим кодом переноса, а в лог — алерт с идентификаторами.
        if (!SalonTimeZone.TryResolve(context.TimeZoneId, out var timeZone))
        {
            _logger.LogError(
                "Salon {SalonId} of barber profile {BarberProfileId} has an unresolvable time zone '{TimeZoneId}'; reschedule slots cannot be calculated.",
                context.SalonId,
                context.BarberProfileId,
                context.TimeZoneId);

            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                $"Salon time zone '{context.TimeZoneId}' cannot be resolved."));
        }

        var today = ScheduleWindowFactory.Today(now, context.TimeZoneId);
        var lastDate = today.AddDays(Constants.BOOKING_HORIZON_DAYS);

        if (date < today || date > lastDate)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.RescheduleDate,
                $"The date must be between {today:yyyy-MM-dd} and {lastDate:yyyy-MM-dd}.",
                nameof(query.Date)));
        }

        var durationMinutes = BookingDuration.MinutesOf(booking);

        if (durationMinutes <= 0)
        {
            _logger.LogError(
                "Booking {BookingId} has a non-positive duration ({DurationMinutes} minutes); reschedule slots cannot be calculated.",
                booking.Id,
                durationMinutes);

            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                "The booking duration is invalid."));
        }

        var dayStart = SalonTimeZone.ToUtc(date.ToDateTime(TimeOnly.MinValue), timeZone);
        var dayEnd = SalonTimeZone.ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);

        if (dayStart is null || dayEnd is null)
        {
            _logger.LogError(
                "Local day boundaries for '{Date}' cannot be converted to UTC in time zone '{TimeZoneId}'.",
                date,
                context.TimeZoneId);

            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                $"Local day boundaries for '{date}' cannot be converted to UTC."));
        }

        var response = new BarberSlotsResponse(
            booking.BarberProfileId,
            booking.SalonId,
            booking.BarberServiceId,
            HairServiceId: null,
            date,
            context.TimeZoneId,
            dayStart.Value,
            dayEnd.Value,
            durationMinutes,
            context.IsWorkingDay,
            []);

        if (!context.IsWorkingDay)
        {
            return response;
        }

        var activeResult = await _bookings.GetActiveByBarberAsync(
            booking.BarberProfileId,
            dayStart.Value,
            dayEnd.Value,
            cancellationToken);
        if (activeResult.IsFailure)
        {
            return activeResult.Error;
        }

        var horizonEnd = now.AddDays(Constants.BOOKING_HORIZON_DAYS);
        var slots = SlotGrid
            .Legal(
                date,
                context.TimeZoneId,
                context.StartTime,
                context.EndTime,
                durationMinutes,
                context.TimeOffs)
            .Where(slot => slot.StartAt > now && slot.StartAt <= horizonEnd)
            .Where(slot => !activeResult.Value.Any(other =>
                other.Id != booking.Id
                && other.StartAt < slot.EndAt
                && slot.StartAt < other.EndAt))
            .ToList();

        return response with { Slots = slots };
    }
}
