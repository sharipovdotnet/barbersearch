using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.RescheduleBooking;

/// <summary>
/// Команда переноса визита: меняются только начало и окончание.
/// </summary>
/// <remarks>
/// Состав полей запроса описан контрактом <c>RescheduleBookingRequest</c>; команда несёт уже
/// разобранное значение и идентификатор из маршрута. Идемпотентного ключа у переноса нет:
/// повтор с тем же временем — безвредный no-op (D15), повтор с новым — новое намерение,
/// а от гонок защищают advisory-блокировка барбера и <c>xmin</c> брони.
/// </remarks>
/// <param name="BookingId">Идентификатор переносимой брони.</param>
/// <param name="StartAt">Новое начало визита (UTC-инстант).</param>
public record RescheduleBookingCommand(Guid BookingId, DateTimeOffset StartAt) : ICommand;
