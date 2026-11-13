using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

namespace BarberSearch.Application.Bookings.GetBookingRescheduleSlots;

/// <summary>
/// Запрос слотов для переноса конкретного визита на локальную дату салона.
/// </summary>
/// <remarks>
/// Отдельное чтение, а не параметр публичных слотов (D7): длительность берётся из самой брони
/// (D6), салон и услуга — тоже из неё, а «игнорируй эту бронь» на публичной ручке стал бы
/// способом перебирать чужие идентификаторы. <see cref="Date"/> необязателен только
/// синтаксически: без него обработчик отвечает <c>400 booking.reschedule.date</c> — так же,
/// как публичные слоты без даты.
/// </remarks>
/// <param name="BookingId">Идентификатор переносимой брони.</param>
/// <param name="Date">Локальная дата салона.</param>
public record GetBookingRescheduleSlotsQuery(
    Guid BookingId,
    DateOnly? Date) : IQuery<BarberSlotsResponse>;
