namespace BarberSearch.Contracts.Bookings.CreateBooking;

/// <summary>
/// Данные запроса на создание брони.
/// </summary>
/// <remarks>
/// Контракт слоя Application: команда <c>CreateBookingCommand</c> несёт его целиком,
/// поэтому четыре подряд идущих идентификатора невозможно перепутать местами при передаче
/// в <c>Booking.Create</c>. Клиент брони в запросе не передаётся: он берётся из токена,
/// поэтому создать бронь от чужого имени нельзя. Ключ идемпотентности обязателен: при обрыве
/// сети мобильный клиент повторяет запрос, и повтор с тем же ключом должен вернуть уже
/// созданную бронь (см. AGENT.md §15.5). Ключ нормализуется (срезаются пробелы по краям) и
/// обязан содержать от 1 до 128 символов: пустой ключ — <c>400 booking.create.idempotencyKey</c>,
/// слишком длинный — <c>400 booking.create.idempotencyKey.tooLong</c>.
/// <para>
/// <paramref name="StartAt"/> и <paramref name="EndAt"/> приходят от клиента, но не
/// произвольны: начало обязано попасть в окно «сейчас − 5 минут … сейчас + 90 дней», а
/// длительность — не превышать 24 часа; иначе ответ <c>400</c>. Выводить
/// <paramref name="EndAt"/> из длительности услуги сервер пока не начал — это отдельный
/// срез (см. CODE-REVIEW.md, R-04/R-20).
/// </para>
/// <para>
/// <paramref name="BarberServiceId"/> — идентификатор строки <c>barber_services</c>
/// («услуга у барбера»), а не <c>hair_services.id</c>: услугу справочника принимают
/// только поиск и слоты (<c>hairServiceId</c>). Пару для брони отдаёт карточка
/// (<c>barberServiceId</c>) и ответ слотов (R-20).
/// </para>
/// </remarks>
public record CreateBookingRequest(
    Guid BarberProfileId,
    Guid BarberServiceId,
    Guid SalonId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string IdempotencyKey,
    string? CustomerNote);
