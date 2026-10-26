using BarberSearch.Domain.Bookings;

namespace BarberSearch.Contracts.Bookings.GetBookings;

/// <summary>
/// Бронь в списке пользователя или администратора.
/// </summary>
/// <remarks>
/// Доменная сущность наружу не отдаётся: в ответе только поля брони и идентификаторы
/// связанных агрегатов, без навигаций и циклических графов.
/// </remarks>
/// <param name="Id">Идентификатор брони.</param>
/// <param name="UserId">Идентификатор клиента.</param>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="BarberServiceId">Идентификатор услуги барбера (<c>barber_services.id</c>).</param>
/// <param name="SalonId">Идентификатор салона.</param>
/// <param name="StartAt">Начало брони в UTC.</param>
/// <param name="EndAt">Окончание брони в UTC.</param>
/// <param name="Price">Цена брони.</param>
/// <param name="Status">Текущий статус брони.</param>
/// <param name="IdempotencyKey">Ключ идемпотентности создания.</param>
/// <param name="CustomerNote">Заметка клиента.</param>
/// <param name="BarberNote">Заметка барбера.</param>
/// <param name="CreatedAt">Когда бронь создана.</param>
public record BookingResponse(
    Guid Id,
    Guid UserId,
    Guid BarberProfileId,
    Guid BarberServiceId,
    Guid SalonId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal Price,
    BookingStatus Status,
    string IdempotencyKey,
    string? CustomerNote,
    string? BarberNote,
    DateTimeOffset CreatedAt);
