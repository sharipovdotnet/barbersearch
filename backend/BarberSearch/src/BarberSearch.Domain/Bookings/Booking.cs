using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Bookings;

/// <summary>
/// Бронь — агрегатный корень. Хранит только идентификаторы барбера, салона и услуги:
/// все они — границы других агрегатов.
/// </summary>
public class Booking : AggregateRoot
{
    /// <summary>
    /// Публичный код ошибки «заметка барбера длиннее предела»; <c>400</c>.
    /// </summary>
    /// <remarks>
    /// Код живёт здесь, потому что правило принадлежит агрегату: домен не ссылается
    /// на <c>BarberSearch.Contracts</c>, где лежит реестр публичных кодов.
    /// </remarks>
    private const string BARBER_NOTE_TOO_LONG_CODE = "booking.barberNote.tooLong";

    /// <summary>
    /// Публичный код ошибки «причина отмены длиннее предела»; <c>400</c>.
    /// </summary>
    private const string CANCELLATION_REASON_TOO_LONG_CODE = "booking.cancel.reasonTooLong";

    /// <summary>
    /// Публичный код ошибки «бронь одновременно и для клиента, и для гостя»; <c>400</c>.
    /// </summary>
    private const string CUSTOMER_AMBIGUOUS_CODE = "booking.create.customerAmbiguous";

    /// <summary>
    /// Публичный код ошибки «имя гостя пустое или длиннее предела»; <c>400</c>.
    /// </summary>
    private const string GUEST_FIRST_NAME_CODE = "booking.create.guestFirstName";

    /// <summary>
    /// Публичный код ошибки «фамилия гостя длиннее предела»; <c>400</c>.
    /// </summary>
    private const string GUEST_LAST_NAME_CODE = "booking.create.guestLastName";

    /// <summary>
    /// Публичный код ошибки «телефон гостя пуст или длиннее предела»; <c>400</c>.
    /// </summary>
    private const string GUEST_PHONE_CODE = "booking.create.guestPhone";

    /// <summary>
    /// Клиент брони; <c>null</c> у записи гостя (walk-in), для которого аккаунт не создаётся.
    /// </summary>
    /// <remarks>
    /// Инвариант «клиент либо зарегистрированный пользователь, либо гость» проверяет
    /// <see cref="Create"/>: при заполненном <see cref="UserId"/> гостевые поля обязаны быть
    /// <c>null</c>, а при <c>null</c> — заполнены имя и телефон гостя. Данные зарегистрированного
    /// клиента остаются в таблице <c>users</c>; гостевые поля — снимок контакта на момент визита.
    /// </remarks>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Имя гостя; <c>null</c> у брони зарегистрированного клиента.
    /// </summary>
    public string? GuestFirstName { get; private set; }

    /// <summary>
    /// Фамилия гостя; <c>null</c>, если она не указана или бронь принадлежит клиенту.
    /// </summary>
    public string? GuestLastName { get; private set; }

    /// <summary>
    /// Телефон гостя — единственный способ связаться с ним до визита.
    /// </summary>
    public string? GuestPhone { get; private set; }

    public Guid BarberProfileId { get; private set; }
    public Guid BarberServiceId { get; private set; }

    /// <summary>
    /// Салон, в котором оказана услуга. Нужен, потому что расписание — принадлежность
    /// пары (барбер, салон).
    /// </summary>
    public Guid SalonId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }

    public decimal Price { get; private set; }

    public BookingStatus Status { get; private set; }

    /// <summary>
    /// Ключ идемпотентности запроса на создание брони: мобильный клиент при обрыве сети
    /// повторяет запрос, и без ключа повтор создаст вторую бронь.
    /// </summary>
    public string IdempotencyKey { get; private set; } = null!;

    public string? CustomerNote { get; private set; }
    public string? BarberNote { get; private set; }

    /// <summary>
    /// Причина отмены, оставленная барбером. Клиент видит её в истории посещений;
    /// отмена самим клиентом причины не требует.
    /// </summary>
    public string? CancellationReason { get; private set; }

    private Booking()
    {
    }

    /// <summary>
    /// Приводит ключ идемпотентности к каноническому виду: срезает пробелы по краям.
    /// </summary>
    /// <remarks>
    /// Канонический вид — часть контракта хранения: по нему строится уникальный индекс
    /// <c>ux_bookings_user_id_idempotency_key</c>, и в нём ключ сравнивается при повторном
    /// запросе. Обработчик нормализует ключ до чтения из репозитория, а <see cref="Create"/>
    /// — при создании агрегата, поэтому оба сравнения используют один и тот же вид (R-09).
    /// </remarks>
    public static string NormalizeIdempotencyKey(string? idempotencyKey)
        => idempotencyKey?.Trim() ?? string.Empty;

    /// <summary>
    /// Проверяет ключ идемпотентности; возвращает <c>null</c>, если он корректен.
    /// </summary>
    /// <remarks>
    /// Проверка вынесена из <see cref="Create"/> в отдельный метод, чтобы обработчик мог
    /// применить её <b>до</b> запроса в репозиторий: иначе пустой ключ отвечал ошибкой
    /// инфраструктуры, а доменный код <c>booking.create.idempotencyKey</c> был недостижим (R-09).
    /// </remarks>
    public static Error? ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Error.Validation(
                "booking.create.idempotencyKey",
                DomainMessages.IDEMPOTENCY_KEY_REQUIRED,
                nameof(idempotencyKey));
        }

        if (idempotencyKey.Length > Constants.MAX_IDEMPOTENCY_KEY_LENGTH)
        {
            return Error.Validation(
                "booking.create.idempotencyKey.tooLong",
                $"""
                 Idempotency key cannot be longer than
                 {Constants.MAX_IDEMPOTENCY_KEY_LENGTH} characters.
                 """,
                nameof(idempotencyKey));
        }

        return null;
    }

    /// <summary>
    /// Создаёт бронь либо для зарегистрированного клиента (<paramref name="userId"/> задан),
    /// либо для гостя (walk-in): тогда <paramref name="userId"/> = <c>null</c>, а имя и телефон
    /// гостя обязательны.
    /// </summary>
    /// <remarks>
    /// Гостевые поля — снимок контакта на момент визита, а не аккаунт: пользователь для гостя
    /// не создаётся. Оба вида одновременно недопустимы (<c>booking.create.customerAmbiguous</c>).
    /// Имена нормализуются (края срезаются, повторные пробелы схлопываются), пустая фамилия
    /// сохраняется как <c>null</c>; телефон только срезается по краям. Ошибки собираются все
    /// сразу, как и в остальных фабриках домена.
    /// </remarks>
    /// <param name="userId">Клиент или <c>null</c> для гостя.</param>
    /// <param name="barberProfileId">Профиль барбера.</param>
    /// <param name="barberServiceId">Услуга барбера (<c>barber_services.id</c>).</param>
    /// <param name="salonId">Салон визита.</param>
    /// <param name="startAt">Начало визита.</param>
    /// <param name="endAt">Окончание визита.</param>
    /// <param name="price">Цена-снимок из <c>BarberService.Price</c>.</param>
    /// <param name="idempotencyKey">Ключ идемпотентности создания.</param>
    /// <param name="customerNote">Заметка клиента.</param>
    /// <param name="guestFirstName">Имя гостя; обязательно, если <paramref name="userId"/> = <c>null</c>.</param>
    /// <param name="guestLastName">Фамилия гостя; необязательна.</param>
    /// <param name="guestPhone">Телефон гостя; обязателен, если <paramref name="userId"/> = <c>null</c>.</param>
    public static Result<Booking, Failure> Create(
        Guid? userId,
        Guid barberProfileId,
        Guid barberServiceId,
        Guid salonId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal price,
        string idempotencyKey,
        string? customerNote = null,
        string? guestFirstName = null,
        string? guestLastName = null,
        string? guestPhone = null)
    {
        var errors = DomainErrors.Collect();

        var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        var normalizedGuestFirstName = NormalizeGuestName(guestFirstName);
        var normalizedGuestLastName = NormalizeGuestName(guestLastName);
        var normalizedGuestPhone = NormalizeGuestPhone(guestPhone);

        if (endAt <= startAt)
        {
            errors.Add(Error.Validation(
                "booking.create.period",
                DomainMessages.END_TIME_NOT_AFTER_START,
                nameof(endAt)));
        }
        else if (endAt - startAt > TimeSpan.FromMinutes(Constants.MAX_BOOKING_DURATION_MINUTES))
        {
            errors.Add(Error.Validation(
                "booking.create.period.tooLong",
                $"Booking duration cannot exceed {Constants.MAX_BOOKING_DURATION_MINUTES} minutes.",
                nameof(endAt)));
        }

        if (price < 0)
        {
            errors.Add(Error.Validation(
                "booking.create.price",
                DomainMessages.PRICE_NEGATIVE,
                nameof(price)));
        }

        if (ValidateIdempotencyKey(normalizedIdempotencyKey) is { } idempotencyKeyError)
        {
            errors.Add(idempotencyKeyError);
        }

        AddCustomerErrors(
            errors,
            userId,
            guestFirstName,
            guestLastName,
            guestPhone,
            normalizedGuestFirstName,
            normalizedGuestLastName,
            normalizedGuestPhone);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new Booking
        {
            UserId = userId,
            GuestFirstName = userId is null ? normalizedGuestFirstName : null,
            GuestLastName = userId is null ? normalizedGuestLastName : null,
            GuestPhone = userId is null ? normalizedGuestPhone : null,
            BarberProfileId = barberProfileId,
            BarberServiceId = barberServiceId,
            SalonId = salonId,
            StartAt = startAt,
            EndAt = endAt,
            Price = price,
            IdempotencyKey = normalizedIdempotencyKey,
            CustomerNote = customerNote,
            Status = BookingStatus.Pending
        };
    }

    public UnitResult<Failure> Confirm()
    {
        if (Status != BookingStatus.Pending)
        {
            return InvalidStatus("booking.confirm.invalidStatus", BookingStatus.Confirmed);
        }

        Status = BookingStatus.Confirmed;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отмена. Недоступна для уже завершённой и уже отменённой брони.
    /// </summary>
    /// <param name="cancellationReason">
    /// Причина отмены, если её указал барбер. Пробелы по краям срезаются, пустая строка
    /// сохраняется как <c>null</c>. Обязательность причины для барбера проверяет Application:
    /// отмена клиентом причины не требует.
    /// </param>
    public UnitResult<Failure> Cancel(string? cancellationReason = null)
    {
        if (Status is BookingStatus.Completed or BookingStatus.Cancelled)
        {
            return InvalidStatus("booking.cancel.invalidStatus", BookingStatus.Cancelled);
        }

        var normalizedReason = string.IsNullOrWhiteSpace(cancellationReason)
            ? null
            : cancellationReason.Trim();

        if (normalizedReason is { Length: > Constants.MAX_CANCELLATION_REASON_LENGTH })
        {
            return DomainErrors.Fail(Error.Validation(
                CANCELLATION_REASON_TOO_LONG_CODE,
                $"Cancellation reason cannot be longer than {Constants.MAX_CANCELLATION_REASON_LENGTH} characters.",
                nameof(cancellationReason)));
        }

        CancellationReason = normalizedReason;
        Status = BookingStatus.Cancelled;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Complete()
    {
        if (Status != BookingStatus.Confirmed)
        {
            return InvalidStatus("booking.complete.invalidStatus", BookingStatus.Completed);
        }

        Status = BookingStatus.Completed;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Клиент не пришёл. Возможно только из подтверждённой брони; правило
    /// «не раньше <see cref="StartAt"/>» проверяется в Application.
    /// </summary>
    public UnitResult<Failure> MarkNoShow()
    {
        if (Status != BookingStatus.Confirmed)
        {
            return InvalidStatus("booking.markNoShow.invalidStatus", BookingStatus.NoShow);
        }

        Status = BookingStatus.NoShow;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Записывает заметку барбера.
    /// </summary>
    /// <remarks>
    /// Заметка нормализуется: пробелы по краям срезаются, пустая строка означает «заметки нет»
    /// и сохраняется как <c>null</c>, чтобы клиент не различал пустую и отсутствующую заметку.
    /// Предел длины проверяется здесь, а не только на входе API: правило принадлежит агрегату.
    /// </remarks>
    /// <param name="note">Текст заметки или <c>null</c>, чтобы очистить её.</param>
    public UnitResult<Failure> AddBarberNote(string? note)
    {
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (normalizedNote is { Length: > Constants.MAX_BARBER_NOTE_LENGTH })
        {
            return DomainErrors.Fail(Error.Validation(
                BARBER_NOTE_TOO_LONG_CODE,
                $"Barber note cannot be longer than {Constants.MAX_BARBER_NOTE_LENGTH} characters.",
                nameof(note)));
        }

        BarberNote = normalizedNote;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Проверяет инвариант «зарегистрированный клиент или гость» и добавляет найденные ошибки.
    /// </summary>
    /// <remarks>
    /// Наличие гостевых полей определяется по исходным значениям, а не по нормализованным:
    /// строка из одних пробелов — это всё ещё попытка передать гостя, и вместе с
    /// <paramref name="userId"/> она даёт <c>booking.create.customerAmbiguous</c>. У гостя
    /// обязательны имя и телефон; пустая фамилия допустима и хранится как <c>null</c>.
    /// </remarks>
    private static void AddCustomerErrors(
        List<Error> errors,
        Guid? userId,
        string? guestFirstName,
        string? guestLastName,
        string? guestPhone,
        string? normalizedGuestFirstName,
        string? normalizedGuestLastName,
        string? normalizedGuestPhone)
    {
        var hasGuestInput = guestFirstName is not null
            || guestLastName is not null
            || guestPhone is not null;

        if (userId is not null)
        {
            if (hasGuestInput)
            {
                errors.Add(Error.Validation(
                    CUSTOMER_AMBIGUOUS_CODE,
                    "A booking cannot belong to a registered customer and a guest at the same time.",
                    nameof(guestFirstName)));
            }

            return;
        }

        if (normalizedGuestFirstName is null)
        {
            errors.Add(Error.Validation(
                GUEST_FIRST_NAME_CODE,
                "Guest first name is required.",
                nameof(guestFirstName)));
        }
        else if (normalizedGuestFirstName.Length > Constants.MAX_GUEST_FIRST_NAME_LENGTH)
        {
            errors.Add(Error.Validation(
                GUEST_FIRST_NAME_CODE,
                $"""
                 Guest first name cannot be longer than
                 {Constants.MAX_GUEST_FIRST_NAME_LENGTH} characters.
                 """,
                nameof(guestFirstName)));
        }

        if (normalizedGuestLastName is { Length: > Constants.MAX_GUEST_LAST_NAME_LENGTH })
        {
            errors.Add(Error.Validation(
                GUEST_LAST_NAME_CODE,
                $"""
                 Guest last name cannot be longer than
                 {Constants.MAX_GUEST_LAST_NAME_LENGTH} characters.
                 """,
                nameof(guestLastName)));
        }

        if (normalizedGuestPhone is null)
        {
            errors.Add(Error.Validation(
                GUEST_PHONE_CODE,
                "Guest phone number is required.",
                nameof(guestPhone)));
        }
        else if (normalizedGuestPhone.Length > Constants.MAX_GUEST_PHONE_LENGTH)
        {
            errors.Add(Error.Validation(
                GUEST_PHONE_CODE,
                $"""
                 Guest phone number cannot be longer than
                 {Constants.MAX_GUEST_PHONE_LENGTH} characters.
                 """,
                nameof(guestPhone)));
        }
    }

    /// <summary>
    /// Имя гостя: края срезаются, повторные пробелы схлопываются, пустая строка — <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Один вид имени нужен и в списках, и в ответах: «Иван  Петров» и «Иван Петров» не должны
    /// выглядеть как разные люди. Зарегистрированных клиентов домен не нормализует — это
    /// правило только для гостевого снимка, которым владеет бронь.
    /// </remarks>
    private static string? NormalizeGuestName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();

        return trimmed.Contains("  ", StringComparison.Ordinal)
            ? string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            : trimmed;
    }

    /// <summary>
    /// Телефон гостя: края срезаются, пустая строка — <c>null</c>. Формат не проверяется:
    /// номера бывают с пробелами, скобками и «+».
    /// </summary>
    private static string? NormalizeGuestPhone(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    private UnitResult<Failure> InvalidStatus(string code, BookingStatus target)
        => new Failure(Error.Conflict(
            code,
            $"Booking in status {Status} cannot be moved to {target}."));
}
