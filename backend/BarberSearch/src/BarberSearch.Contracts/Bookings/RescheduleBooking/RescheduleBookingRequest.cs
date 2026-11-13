namespace BarberSearch.Contracts.Bookings.RescheduleBooking;

/// <summary>
/// Данные запроса на перенос визита.
/// </summary>
/// <remarks>
/// Перенос меняет только время: услуга, салон, барбер, цена и статус остаются прежними,
/// поэтому в теле передаётся одно поле. <see cref="StartAt"/> обязан быть началом слота
/// из <c>GET api/bookings/{id}/reschedule-slots</c>: сетка считается по длительности самой
/// брони, а не по текущему прайсу, и «полуслоты» календарь не принимает. Новое время
/// строго в будущем и не дальше горизонта записи (<c>90</c> дней); иначе — <c>400</c>
/// с кодом <c>booking.reschedule.startAt.*</c>. Успех — <c>204</c> без тела: новое время
/// клиент уже знает из слота, а сервер перечитывается опросом.
/// </remarks>
/// <param name="StartAt">Новое начало визита (UTC-инстант).</param>
public record RescheduleBookingRequest(DateTimeOffset StartAt);
