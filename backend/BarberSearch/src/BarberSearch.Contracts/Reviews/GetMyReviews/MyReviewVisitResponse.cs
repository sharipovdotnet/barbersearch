namespace BarberSearch.Contracts.Reviews.GetMyReviews;

/// <summary>
/// Визит, по которому автор оставил отзыв.
/// </summary>
/// <remarks>
/// Время отдаётся инстантом вместе с IANA-поясом салона: клиент показывает его в поясе
/// визита, а не в поясе браузера, и не зависит от часового пояса сервера.
/// </remarks>
/// <param name="BookingId">Идентификатор брони.</param>
/// <param name="StartAt">Начало визита — ISO-инстант с offset.</param>
/// <param name="TimeZoneId">IANA-идентификатор часового пояса салона.</param>
/// <param name="Price">Стоимость визита.</param>
/// <param name="ServiceName">Название услуги из справочника.</param>
/// <param name="SalonName">Название салона.</param>
public record MyReviewVisitResponse(
    Guid BookingId,
    DateTimeOffset StartAt,
    string TimeZoneId,
    decimal Price,
    string ServiceName,
    string SalonName);
