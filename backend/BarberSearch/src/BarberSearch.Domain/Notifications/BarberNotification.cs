using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.Notifications;

/// <summary>
/// Уведомление барберу — сущность инбокса, а не агрегат: снаружи ею управляют только
/// переходы доставки и отметка прочтения, вложенных сущностей у неё нет.
/// </summary>
/// <remarks>
/// <para>
/// Уведомление — снимок на момент создания: <see cref="ServiceName"/>, <see cref="SalonName"/>,
/// <see cref="SalonTimeZoneId"/> и <see cref="StartAt"/> не пересчитываются, если услугу
/// переименовали или салон сменил пояс. Клиенту важно то, что было актуально тогда.
/// </para>
/// <para>
/// Персональных данных клиента здесь нет и быть не должно (152-ФЗ): хранятся только время,
/// услуга и салон. Поэтому текст сообщения в Telegram тоже не может содержать имя,
/// телефон или заметку клиента (AGENT.md §13, спецификация варианта C, D3).
/// </para>
/// <para>
/// Время — <see cref="DateTimeOffset"/> в UTC, как и во всём домене (AGENT.md §10.11);
/// «сейчас» домен не читает, его передаёт Application (<see cref="MarkRead"/>), потому что
/// это бизнес-решение, а не техническая метка (AGENT.md §7.6).
/// </para>
/// </remarks>
public class BarberNotification : Entity
{
    /// <summary>
    /// Публичный код ошибки «набор полей не соответствует виду уведомления»; <c>400</c>.
    /// </summary>
    /// <remarks>
    /// Код общий для обеих фабрик: клиент не различает, какое именно поле забыли, а
    /// <c>InvalidField</c> в ошибке указывает на конкретное. Пределы длины снимков живут
    /// в отдельных кодах — там подсказка важна.
    /// </remarks>
    private const string CREATE_INVALID_CODE = "notification.create.invalid";

    /// <summary>
    /// Публичный код ошибки «снимок услуги длиннее предела»; <c>400</c>.
    /// </summary>
    private const string SERVICE_NAME_CODE = "notification.create.serviceName";

    /// <summary>
    /// Публичный код ошибки «снимок салона длиннее предела»; <c>400</c>.
    /// </summary>
    private const string SALON_NAME_CODE = "notification.create.salonName";

    /// <summary>
    /// Публичный код ошибки «снимок пояса салона длиннее предела»; <c>400</c>.
    /// </summary>
    private const string TIME_ZONE_CODE = "notification.create.salonTimeZoneId";

    /// <summary>
    /// Получатель: пользователь-барбер. Один барбер может работать в нескольких салонах,
    /// поэтому дайджест считается на него, а не на салон (решение D5).
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Вид уведомления: напоминание о визите или утренний дайджест.
    /// </summary>
    public BarberNotificationKind Kind { get; private set; }

    /// <summary>
    /// Бронь, о которой напоминаем; обязательна для <see cref="BarberNotificationKind.Upcoming"/>.
    /// </summary>
    /// <remarks>
    /// Вместе с <see cref="Kind"/> образует ключ дедупликации: частичный уникальный индекс
    /// <c>ux_barber_notifications_upcoming</c> не даёт создать второе напоминание о том же
    /// визите, поэтому повторный тик воркера безопасен.
    /// </remarks>
    public Guid? BookingId { get; private set; }

    /// <summary>
    /// Начало визита в UTC; для дайджеста — начало первой записи дня.
    /// </summary>
    public DateTimeOffset? StartAt { get; private set; }

    /// <summary>
    /// Снимок названия услуги; <c>null</c> — услуга на момент создания была недоступна.
    /// </summary>
    public string? ServiceName { get; private set; }

    /// <summary>
    /// Снимок названия салона; <c>null</c> — салон на момент создания был недоступен.
    /// </summary>
    public string? SalonName { get; private set; }

    /// <summary>
    /// Снимок пояса салона (IANA). По нему время в тексте напоминания показывается в поясе
    /// салона, а не сервера, — даже если салон позже сменил пояс.
    /// </summary>
    public string? SalonTimeZoneId { get; private set; }

    /// <summary>
    /// Число подтверждённых записей в локальных сутках салона; обязательно для дайджеста.
    /// </summary>
    public int? BookingsCount { get; private set; }

    /// <summary>
    /// Локальная дата салона, за которую собран дайджест; обязательна для дайджеста.
    /// </summary>
    /// <remarks>
    /// Вместе с <see cref="UserId"/> образует ключ дедупликации <c>ux_barber_notifications_digest</c>:
    /// дайджест приходит один раз в день на барбера, а не на салон.
    /// </remarks>
    public DateOnly? DigestLocalDate { get; private set; }

    /// <summary>
    /// Момент прочтения в инбоксе; <c>null</c> — уведомление ещё не прочитано.
    /// </summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>
    /// Состояние доставки. Новое уведомление всегда <see cref="BarberNotificationDelivery.Pending"/>.
    /// </summary>
    public BarberNotificationDelivery Delivery { get; private set; }

    /// <summary>
    /// Число неудачных попыток доставки, <c>0..Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS</c>.
    /// Растёт только в <see cref="MarkFailed"/>.
    /// </summary>
    public int DeliveryAttempts { get; private set; }

    /// <summary>
    /// Момент следующей попытки доставки после ошибки; <c>null</c> — попыток больше не будет
    /// или их ещё не было. Выборка пачки фильтрует
    /// <c>NextAttemptAt is null or NextAttemptAt &lt;= now</c>.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>
    /// Текст последней ошибки доставки — техническая диагностика без ПДн.
    /// </summary>
    public string? LastError { get; private set; }

    private BarberNotification()
    {
    }

    /// <summary>
    /// Создаёт напоминание о визите.
    /// </summary>
    /// <remarks>
    /// Обязательны <paramref name="bookingId"/> и <paramref name="startAt"/>: напоминание
    /// без визита нечем показать. Снимки услуги и салона необязательны, но их пределы
    /// проверяются здесь: строка сверх предела не пролезет в колонку.
    /// </remarks>
    /// <param name="userId">Барбер-получатель.</param>
    /// <param name="bookingId">Бронь, о которой напоминаем.</param>
    /// <param name="startAt">Начало визита в UTC.</param>
    /// <param name="serviceName">Снимок названия услуги.</param>
    /// <param name="salonName">Снимок названия салона.</param>
    /// <param name="salonTimeZoneId">Снимок пояса салона (IANA).</param>
    public static Result<BarberNotification, Failure> Upcoming(
        Guid userId,
        Guid bookingId,
        DateTimeOffset startAt,
        string? serviceName,
        string? salonName,
        string? salonTimeZoneId)
    {
        var errors = DomainErrors.Collect();

        if (userId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "Notification recipient cannot be empty.",
                nameof(userId)));
        }

        if (bookingId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "An upcoming notification requires a booking.",
                nameof(bookingId)));
        }

        if (startAt == default)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "An upcoming notification requires the visit start time.",
                nameof(startAt)));
        }

        AddSnapshotErrors(errors, serviceName, salonName, salonTimeZoneId);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new BarberNotification
        {
            UserId = userId,
            Kind = BarberNotificationKind.Upcoming,
            BookingId = bookingId,
            StartAt = startAt,
            ServiceName = NormalizeSnapshot(serviceName),
            SalonName = NormalizeSnapshot(salonName),
            SalonTimeZoneId = NormalizeSnapshot(salonTimeZoneId),
            Delivery = BarberNotificationDelivery.Pending
        };
    }

    /// <summary>
    /// Создаёт утренний дайджест за локальные сутки салона.
    /// </summary>
    /// <remarks>
    /// <paramref name="bookingsCount"/> = 0 допустим как значение, но такой дайджест
    /// Application не создаёт: сообщать нечего. <paramref name="firstStartAt"/> и снимки
    /// первой записи необязательны — они нужны только для текста сообщения.
    /// </remarks>
    /// <param name="userId">Барбер-получатель.</param>
    /// <param name="bookingsCount">Число подтверждённых записей за локальные сутки салона.</param>
    /// <param name="digestLocalDate">Локальная дата салона.</param>
    /// <param name="firstStartAt">Начало первой записи дня в UTC.</param>
    /// <param name="serviceName">Снимок услуги первой записи.</param>
    /// <param name="salonName">Снимок салона первой записи.</param>
    /// <param name="salonTimeZoneId">Снимок пояса салона (IANA).</param>
    public static Result<BarberNotification, Failure> Digest(
        Guid userId,
        int bookingsCount,
        DateOnly digestLocalDate,
        DateTimeOffset? firstStartAt,
        string? serviceName,
        string? salonName,
        string? salonTimeZoneId)
    {
        var errors = DomainErrors.Collect();

        if (userId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "Notification recipient cannot be empty.",
                nameof(userId)));
        }

        if (bookingsCount < 0)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "The number of bookings cannot be negative.",
                nameof(bookingsCount)));
        }

        if (digestLocalDate == default)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "A digest notification requires the salon local date.",
                nameof(digestLocalDate)));
        }

        AddSnapshotErrors(errors, serviceName, salonName, salonTimeZoneId);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new BarberNotification
        {
            UserId = userId,
            Kind = BarberNotificationKind.Digest,
            StartAt = firstStartAt,
            ServiceName = NormalizeSnapshot(serviceName),
            SalonName = NormalizeSnapshot(salonName),
            SalonTimeZoneId = NormalizeSnapshot(salonTimeZoneId),
            BookingsCount = bookingsCount,
            DigestLocalDate = digestLocalDate,
            Delivery = BarberNotificationDelivery.Pending
        };
    }

    /// <summary>
    /// Отмечает доставку успешной. Допустимо только из
    /// <see cref="BarberNotificationDelivery.Pending"/>.
    /// </summary>
    public UnitResult<Failure> MarkSent()
    {
        if (Delivery != BarberNotificationDelivery.Pending)
        {
            return InvalidTransition("notification.markSent.invalidStatus", BarberNotificationDelivery.Sent);
        }

        Delivery = BarberNotificationDelivery.Sent;
        NextAttemptAt = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отмечает, что доставка не нужна или невозможна. Допустимо только из
    /// <see cref="BarberNotificationDelivery.Pending"/>; состояние терминальное.
    /// </summary>
    /// <param name="reason">
    /// Причина пропуска без ПДн, например <c>booking is not confirmed</c>. Пробелы по краям
    /// срезаются, пустая строка сохраняется как <c>null</c>.
    /// </param>
    public UnitResult<Failure> MarkSkipped(string? reason)
    {
        if (Delivery != BarberNotificationDelivery.Pending)
        {
            return InvalidTransition(
                "notification.markSkipped.invalidStatus",
                BarberNotificationDelivery.Skipped);
        }

        Delivery = BarberNotificationDelivery.Skipped;
        LastError = NormalizeError(reason);
        NextAttemptAt = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Записывает неудачную попытку доставки и увеличивает счётчик попыток.
    /// </summary>
    /// <remarks>
    /// Пока попыток меньше <c>Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS</c>, уведомление
    /// остаётся <see cref="BarberNotificationDelivery.Pending"/> и получит
    /// <see cref="NextAttemptAt"/> — повтор запланирован. На последней попытке состояние
    /// становится <see cref="BarberNotificationDelivery.Failed"/>, и <see cref="NextAttemptAt"/>
    /// очищается: повторов больше нет. Расписание повторов (1/5/15 минут) — решение
    /// Application, поэтому момент следующей попытки передаётся параметром.
    /// </remarks>
    /// <param name="error">Текст ошибки без ПДн; обрезается до предела колонки.</param>
    /// <param name="nextAttemptAt">Момент следующей попытки; игнорируется на последней попытке.</param>
    public UnitResult<Failure> MarkFailed(string? error, DateTimeOffset? nextAttemptAt = null)
    {
        if (Delivery != BarberNotificationDelivery.Pending)
        {
            return InvalidTransition(
                "notification.markFailed.invalidStatus",
                BarberNotificationDelivery.Failed);
        }

        DeliveryAttempts++;
        LastError = NormalizeError(error);

        if (DeliveryAttempts >= Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS)
        {
            Delivery = BarberNotificationDelivery.Failed;
            NextAttemptAt = null;
        }
        else
        {
            NextAttemptAt = nextAttemptAt;
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отмечает уведомление прочитанным. Идемпотентно: повторный вызов не меняет
    /// <see cref="ReadAt"/> и не двигает метку изменения.
    /// </summary>
    /// <param name="readAt">
    /// Момент прочтения в UTC. Часы передаёт Application: домен бизнес-«сейчас» не читает
    /// (AGENT.md §7.6).
    /// </param>
    public UnitResult<Failure> MarkRead(DateTimeOffset readAt)
    {
        if (readAt == default)
        {
            return DomainErrors.Fail(Error.Validation(
                "notification.markRead.readAt",
                "The read time cannot be empty.",
                nameof(readAt)));
        }

        if (ReadAt is not null)
        {
            return DomainErrors.Ok();
        }

        ReadAt = readAt;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Добавляет ошибки пределов длины снимков — общие для обеих фабрик.
    /// </summary>
    private static void AddSnapshotErrors(
        List<Error> errors,
        string? serviceName,
        string? salonName,
        string? salonTimeZoneId)
    {
        var normalizedServiceName = NormalizeSnapshot(serviceName);
        if (normalizedServiceName is { Length: > Constants.MAX_NOTIFICATION_SERVICE_NAME_LENGTH })
        {
            errors.Add(Error.Validation(
                SERVICE_NAME_CODE,
                $"""
                 The service name snapshot cannot be longer than
                 {Constants.MAX_NOTIFICATION_SERVICE_NAME_LENGTH} characters.
                 """,
                nameof(serviceName)));
        }

        var normalizedSalonName = NormalizeSnapshot(salonName);
        if (normalizedSalonName is { Length: > Constants.MAX_NOTIFICATION_SALON_NAME_LENGTH })
        {
            errors.Add(Error.Validation(
                SALON_NAME_CODE,
                $"""
                 The salon name snapshot cannot be longer than
                 {Constants.MAX_NOTIFICATION_SALON_NAME_LENGTH} characters.
                 """,
                nameof(salonName)));
        }

        var normalizedTimeZoneId = NormalizeSnapshot(salonTimeZoneId);
        if (normalizedTimeZoneId is { Length: > Constants.MAX_NOTIFICATION_TIME_ZONE_ID_LENGTH })
        {
            errors.Add(Error.Validation(
                TIME_ZONE_CODE,
                $"""
                 The salon time zone snapshot cannot be longer than
                 {Constants.MAX_NOTIFICATION_TIME_ZONE_ID_LENGTH} characters.
                 """,
                nameof(salonTimeZoneId)));
        }
    }

    /// <summary>
    /// Приводит снимок к каноническому виду: пробелы по краям срезаются, пустая строка
    /// означает «значения нет» и хранится как <c>null</c>.
    /// </summary>
    private static string? NormalizeSnapshot(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Нормализует текст ошибки доставки и обрезает его до предела колонки.
    /// </summary>
    /// <remarks>
    /// Обрезка, а не отказ: текст ошибки приходит от внешнего сервиса, и слишком длинное
    /// сообщение не должно мешать зафиксировать саму неудачу.
    /// </remarks>
    private static string? NormalizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var trimmed = error.Trim();

        return trimmed.Length <= Constants.MAX_NOTIFICATION_LAST_ERROR_LENGTH
            ? trimmed
            : trimmed[..Constants.MAX_NOTIFICATION_LAST_ERROR_LENGTH];
    }

    private UnitResult<Failure> InvalidTransition(
        string code,
        BarberNotificationDelivery target)
        => new Failure(Error.Conflict(
            code,
            $"Notification in delivery state {Delivery} cannot be moved to {target}."));
}
