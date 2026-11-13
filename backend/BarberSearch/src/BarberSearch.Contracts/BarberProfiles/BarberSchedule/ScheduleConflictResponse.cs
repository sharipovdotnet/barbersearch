using BarberSearch.Domain.Bookings;

namespace BarberSearch.Contracts.BarberProfiles.BarberSchedule;

/// <summary>
/// Затронутый визит в ответе <c>409 barberSchedule.conflict</c>: что именно окажется вне
/// расписания, если правку всё-таки сохранить.
/// </summary>
/// <remarks>
/// Полей ровно столько, сколько нужно диалогу подтверждения: время в поясе конфликта, имя
/// клиента, услуга и статус. Телефон, email, <c>userId</c>, заметки и цены сюда не попадают:
/// список — объяснение, а не рабочая карточка визита.
/// </remarks>
/// <param name="Id">Идентификатор брони.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="EndAt">Окончание визита в UTC.</param>
/// <param name="TimeZoneId">Пояс салона, в котором время показывается клиенту.</param>
/// <param name="CustomerName">Имя клиента (или «Гость» для записи без имени).</param>
/// <param name="ServiceName">Название услуги, записанной в визите.</param>
/// <param name="Status">Статус брони на момент проверки: <c>Pending</c> или <c>Confirmed</c>.</param>
public record ScheduleConflictResponse(
    Guid Id,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string TimeZoneId,
    string CustomerName,
    string ServiceName,
    BookingStatus Status);
