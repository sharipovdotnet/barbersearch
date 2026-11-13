using BarberSearch.Domain.Bookings;

namespace BarberSearch.Application.Common;

/// <summary>
/// Длительность визита — единственное, что перенос сохраняет от прежней брони.
/// </summary>
/// <remarks>
/// Считается по самой брони, а не по текущему прайсу (D6): барбер мог изменить длительность
/// услуги после записи, а обещание клиенту остаётся прежним. Округление до целой минуты —
/// потому что шаг сетки слотов целочисленный; неположительный результат означает повреждённые
/// данные, и решение «какой код вернуть» принимает вызывающий обработчик.
/// </remarks>
public static class BookingDuration
{
    /// <summary>
    /// Длительность визита в минутах, округлённая до целого.
    /// </summary>
    /// <param name="booking">Бронь, чьи <c>StartAt</c>/<c>EndAt</c> задают интервал.</param>
    public static int MinutesOf(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return (int)Math.Round((booking.EndAt - booking.StartAt).TotalMinutes);
    }
}
