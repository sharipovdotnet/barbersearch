namespace BarberSearch.Contracts.Reviews.Admin;

/// <summary>
/// Визит, по которому оставлен отзыв.
/// </summary>
/// <param name="BookingId">Идентификатор брони.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="Price">Стоимость визита.</param>
/// <param name="ServiceName">Название услуги из справочника.</param>
/// <param name="SalonName">Название салона.</param>
/// <param name="TimeZoneId">IANA-идентификатор часового пояса салона.</param>
public record AdminReviewVisitResponse(
    Guid BookingId,
    DateTimeOffset StartAt,
    decimal Price,
    string ServiceName,
    string SalonName,
    string TimeZoneId);
