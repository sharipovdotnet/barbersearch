namespace BarberSearch.Contracts.Bookings.CreateBooking;

/// <summary>
/// Данные запроса на создание брони.
/// </summary>
/// <remarks>
/// Контракт слоя Application: команда <c>CreateBookingCommand</c> несёт его целиком,
/// поэтому подряд идущие идентификаторы невозможно перепутать местами при передаче
/// в <c>Booking.Create</c>. Клиент брони в запросе не передаётся: он берётся из токена,
/// поэтому создать бронь от чужого имени нельзя. Ключ идемпотентности обязателен: при обрыве
/// сети мобильный клиент повторяет запрос, и повтор с тем же ключом должен вернуть уже
/// созданную бронь (см. AGENT.md §15.5). Ключ нормализуется (срезаются пробелы по краям) и
/// обязан содержать от 1 до 128 символов: пустой ключ — <c>400 booking.create.idempotencyKey</c>,
/// слишком длинный — <c>400 booking.create.idempotencyKey.tooLong</c>.
/// <para>
/// Время визита задаёт только <paramref name="StartAt"/>: окончание сервер выводит из
/// <c>BarberService.DurationMinutes</c> той услуги, которую выбрал клиент. Поэтому «записаться
/// на 30 минут, а занять четыре часа» нельзя, а перенос/смена прайса не меняют уже обещанную
/// длительность — она живёт в самой брони. <paramref name="StartAt"/> обязан попасть в окно
/// «сейчас − 5 минут … сейчас + 90 дней» и совпасть со свободным слотом
/// <c>GET api/barbers/{id}/slots</c>; иначе ответ <c>409</c>
/// (<c>booking.create.startAt.notASlot</c> / <c>.outsideSchedule</c> / <c>booking.add.overlap</c>).
/// Старый клиент, который всё ещё присылает <c>endAt</c>, не ломается: неизвестное поле
/// игнорируется десериализатором, а конец считает сервер.
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
    string IdempotencyKey,
    string? CustomerNote);
