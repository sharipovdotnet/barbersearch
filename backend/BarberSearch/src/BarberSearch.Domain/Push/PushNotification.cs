using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.Push;

/// <summary>
/// Push-уведомление клиенту о визите — строка outbox-очереди.
/// </summary>
/// <remarks>
/// <para>
/// Это не инбокс: у клиента в этом срезе нет списка уведомлений, строка живёт ровно до
/// доставки и удаляется ретенцией (<c>Push:RetentionDays</c>, 30 дней). Поэтому сущность —
/// <see cref="Entity"/>, а не агрегат: вложенных сущностей нет, снаружи доступны только
/// переходы доставки.
/// </para>
/// <para>
/// Снимок на момент создания: <see cref="SalonName"/>, <see cref="BarberName"/>,
/// <see cref="ServiceName"/>, <see cref="SalonTimeZoneId"/> и <see cref="StartAt"/> не
/// пересчитываются, если услугу переименовали, мастер сменил имя или салон — пояс. Клиенту
/// важно то, что было актуально тогда, поэтому внешних ключей на каталог у таблицы нет.
/// </para>
/// <para>
/// Персональных данных клиента здесь нет и быть не должно (152-ФЗ): ни имени, ни телефона,
/// ни заметки — только салон, мастер, услуга и время. Текст сообщения собирается из этих
/// полей (<c>PushMessages</c>), поэтому ПДн в push физически неоткуда взяться.
/// </para>
/// <para>
/// «Сейчас» домен не читает: момент следующей попытки и предел попыток приходят аргументами
/// от Application (AGENT.md §7.6). Уникальный индекс <c>ux_push_notifications_booking_id</c>
/// по паре <c>(booking_id, kind)</c> держит правило «одно уведомление каждого вида на визит»
/// на уровне БД, поэтому повторный тик и второй экземпляр приложения дублей не создают,
/// а напоминание и событие создания одной брони сосуществуют.
/// </para>
/// </remarks>
public sealed class PushNotification : Entity
{
    /// <summary>
    /// Публичный код ошибки «набор полей не соответствует напоминанию»; <c>400</c>.
    /// </summary>
    private const string CREATE_INVALID_CODE = "pushNotification.create.invalid";

    /// <summary>
    /// Публичный код ошибки «снимок услуги длиннее предела»; <c>400</c>.
    /// </summary>
    private const string SERVICE_NAME_CODE = "pushNotification.create.serviceName";

    /// <summary>
    /// Публичный код ошибки «снимок салона длиннее предела»; <c>400</c>.
    /// </summary>
    private const string SALON_NAME_CODE = "pushNotification.create.salonName";

    /// <summary>
    /// Публичный код ошибки «снимок имени мастера длиннее предела»; <c>400</c>.
    /// </summary>
    private const string BARBER_NAME_CODE = "pushNotification.create.barberName";

    /// <summary>
    /// Публичный код ошибки «снимок пояса салона длиннее предела»; <c>400</c>.
    /// </summary>
    private const string TIME_ZONE_CODE = "pushNotification.create.salonTimeZoneId";

    /// <summary>
    /// Вид уведомления: напоминание или событие создания брони.
    /// </summary>
    /// <remarks>
    /// Вместе с <see cref="BookingId"/> входит в уникальный индекс
    /// <c>ux_push_notifications_booking_id</c>: на один визит — не больше одного уведомления
    /// каждого вида.
    /// </remarks>
    public PushNotificationKind Kind { get; private set; }

    /// <summary>
    /// Получатель: клиент, записавшийся на визит.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Бронь, к которой относится уведомление; в паре с <see cref="Kind"/> уникальна.
    /// </summary>
    public Guid BookingId { get; private set; }

    /// <summary>
    /// Начало визита в UTC — снимок на момент создания строки.
    /// </summary>
    public DateTimeOffset StartAt { get; private set; }

    /// <summary>
    /// Снимок названия салона; <c>null</c> — салон на момент создания был недоступен.
    /// </summary>
    public string? SalonName { get; private set; }

    /// <summary>
    /// Снимок имени мастера; <c>null</c> — имя на момент создания было недоступно.
    /// </summary>
    public string? BarberName { get; private set; }

    /// <summary>
    /// Снимок названия услуги; <c>null</c> — услуга на момент создания была недоступна.
    /// </summary>
    public string? ServiceName { get; private set; }

    /// <summary>
    /// Снимок пояса салона (IANA). По нему время в тексте показывается в поясе салона,
    /// а слово «сегодня»/«завтра» считается от локальной даты салона; неизвестный пояс
    /// даёт UTC, а не отказ от отправки.
    /// </summary>
    public string? SalonTimeZoneId { get; private set; }

    /// <summary>
    /// Состояние доставки. Новое напоминание всегда <see cref="PushDelivery.Pending"/>.
    /// </summary>
    public PushDelivery Delivery { get; private set; }

    /// <summary>
    /// Число неудачных попыток доставки, <c>0..Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS</c>.
    /// Растёт только в <see cref="MarkFailed"/>.
    /// </summary>
    public int DeliveryAttempts { get; private set; }

    /// <summary>
    /// Момент следующей попытки после ошибки; <c>null</c> — повторов больше нет или их ещё
    /// не было. Выборка пачки фильтрует <c>next_attempt_at is null or next_attempt_at &lt;= now</c>.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>
    /// Код последней ошибки доставки — техническая диагностика без токенов и без текста
    /// сообщения.
    /// </summary>
    public string? LastError { get; private set; }

    private PushNotification()
    {
    }

    /// <summary>
    /// Создаёт уведомление о визите заданного вида.
    /// </summary>
    /// <remarks>
    /// Обязательны <paramref name="kind"/>, <paramref name="userId"/>, <paramref name="bookingId"/>
    /// и <paramref name="startAt"/>: уведомление без получателя, визита, времени или вида нечем
    /// отправить и не по чему перепроверить. Снимки необязательны, но их пределы проверяются
    /// здесь: строка сверх предела не пролезет в колонку.
    /// </remarks>
    /// <param name="kind">Вид уведомления.</param>
    /// <param name="userId">Клиент-получатель.</param>
    /// <param name="bookingId">Бронь, к которой относится уведомление.</param>
    /// <param name="startAt">Начало визита в UTC.</param>
    /// <param name="salonName">Снимок названия салона.</param>
    /// <param name="barberName">Снимок имени мастера.</param>
    /// <param name="serviceName">Снимок названия услуги.</param>
    /// <param name="salonTimeZoneId">Снимок пояса салона (IANA).</param>
    public static Result<PushNotification, Failure> Create(
        PushNotificationKind kind,
        Guid userId,
        Guid bookingId,
        DateTimeOffset startAt,
        string? salonName,
        string? barberName,
        string? serviceName,
        string? salonTimeZoneId)
    {
        var errors = DomainErrors.Collect();

        if (!Enum.IsDefined(kind))
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "Push notification kind is not defined.",
                nameof(kind)));
        }

        if (userId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "Push notification recipient cannot be empty.",
                nameof(userId)));
        }

        if (bookingId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "A push notification requires a booking.",
                nameof(bookingId)));
        }

        if (startAt == default)
        {
            errors.Add(Error.Validation(
                CREATE_INVALID_CODE,
                "A push notification requires the visit start time.",
                nameof(startAt)));
        }

        AddSnapshotErrors(errors, salonName, barberName, serviceName, salonTimeZoneId);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new PushNotification
        {
            Kind = kind,
            UserId = userId,
            BookingId = bookingId,
            StartAt = startAt,
            SalonName = NormalizeSnapshot(salonName),
            BarberName = NormalizeSnapshot(barberName),
            ServiceName = NormalizeSnapshot(serviceName),
            SalonTimeZoneId = NormalizeSnapshot(salonTimeZoneId),
            Delivery = PushDelivery.Pending
        };
    }

    /// <summary>
    /// Отмечает доставку успешной: хотя бы одно устройство приняло сообщение.
    /// </summary>
    /// <remarks>
    /// Допустимо только из <see cref="PushDelivery.Pending"/>: терминальные состояния
    /// не переоткрываются. Мультиустройство не меняет правило — успех любого устройства
    /// означает, что напоминание доставлено.
    /// </remarks>
    public UnitResult<Failure> MarkSent()
    {
        if (Delivery != PushDelivery.Pending)
        {
            return InvalidTransition("pushNotification.markSent.invalidStatus", PushDelivery.Sent);
        }

        Delivery = PushDelivery.Sent;
        NextAttemptAt = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отмечает, что доставка не нужна или невозможна.
    /// </summary>
    /// <remarks>
    /// Причина — стабильная строка без ПДн и без токена, например <c>booking is not confirmed</c>;
    /// она сохраняется в <see cref="LastError"/> как диагностика. Состояние терминальное.
    /// </remarks>
    /// <param name="reason">Причина пропуска без ПДн; пустая строка сохраняется как <c>null</c>.</param>
    public UnitResult<Failure> MarkSkipped(string? reason)
    {
        if (Delivery != PushDelivery.Pending)
        {
            return InvalidTransition(
                "pushNotification.markSkipped.invalidStatus",
                PushDelivery.Skipped);
        }

        Delivery = PushDelivery.Skipped;
        LastError = NormalizeError(reason);
        NextAttemptAt = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Записывает неудачную попытку доставки и увеличивает счётчик попыток.
    /// </summary>
    /// <remarks>
    /// Пока попыток меньше <c>Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS</c> (5), строка
    /// остаётся <see cref="PushDelivery.Pending"/> и получает <see cref="NextAttemptAt"/> —
    /// повтор запланирован. На пятой неудаче состояние становится
    /// <see cref="PushDelivery.Failed"/>, и <see cref="NextAttemptAt"/> очищается: повторов
    /// больше нет. Расписание 1/5/15 минут — решение Application, поэтому момент следующей
    /// попытки передаётся параметром.
    /// </remarks>
    /// <param name="error">Код ошибки без токенов и текста сообщения; обрезается до предела колонки.</param>
    /// <param name="nextAttemptAt">Момент следующей попытки; игнорируется на последней попытке.</param>
    /// <param name="maxAttempts">
    /// Бюджет попыток из <c>Push:MaxAttempts</c>. Значение ограничивается доменным пределом
    /// <c>Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS</c>: выше него не пустят ни схема
    /// (<c>delivery_attempts &lt;= 5</c>), ни выборка пачки. Меньшее значение позволяет
    /// сократить бюджет повторов конфигурацией, не меняя код.
    /// </param>
    public UnitResult<Failure> MarkFailed(
        string? error,
        DateTimeOffset? nextAttemptAt = null,
        int maxAttempts = Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS)
    {
        if (Delivery != PushDelivery.Pending)
        {
            return InvalidTransition(
                "pushNotification.markFailed.invalidStatus",
                PushDelivery.Failed);
        }

        var effectiveMaxAttempts = Math.Clamp(
            maxAttempts,
            1,
            Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS);

        DeliveryAttempts++;
        LastError = NormalizeError(error);

        if (DeliveryAttempts >= effectiveMaxAttempts)
        {
            Delivery = PushDelivery.Failed;
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
    /// Добавляет ошибки пределов длины снимков.
    /// </summary>
    private static void AddSnapshotErrors(
        List<Error> errors,
        string? salonName,
        string? barberName,
        string? serviceName,
        string? salonTimeZoneId)
    {
        if (NormalizeSnapshot(serviceName) is { Length: > Constants.MAX_NOTIFICATION_SERVICE_NAME_LENGTH })
        {
            errors.Add(Error.Validation(
                SERVICE_NAME_CODE,
                $"""
                 The service name snapshot cannot be longer than
                 {Constants.MAX_NOTIFICATION_SERVICE_NAME_LENGTH} characters.
                 """,
                nameof(serviceName)));
        }

        if (NormalizeSnapshot(salonName) is { Length: > Constants.MAX_NOTIFICATION_SALON_NAME_LENGTH })
        {
            errors.Add(Error.Validation(
                SALON_NAME_CODE,
                $"""
                 The salon name snapshot cannot be longer than
                 {Constants.MAX_NOTIFICATION_SALON_NAME_LENGTH} characters.
                 """,
                nameof(salonName)));
        }

        if (NormalizeSnapshot(barberName) is { Length: > Constants.MAX_NOTIFICATION_BARBER_NAME_LENGTH })
        {
            errors.Add(Error.Validation(
                BARBER_NAME_CODE,
                $"""
                 The barber name snapshot cannot be longer than
                 {Constants.MAX_NOTIFICATION_BARBER_NAME_LENGTH} characters.
                 """,
                nameof(barberName)));
        }

        if (NormalizeSnapshot(salonTimeZoneId) is { Length: > Constants.MAX_NOTIFICATION_TIME_ZONE_ID_LENGTH })
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
    /// Обрезка, а не отказ: текст приходит от внешнего сервиса, и слишком длинное сообщение
    /// не должно мешать зафиксировать саму неудачу.
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

    private UnitResult<Failure> InvalidTransition(string code, PushDelivery target)
        => new Failure(Error.Conflict(
            code,
            $"Push notification in delivery state {Delivery} cannot be moved to {target}."));
}
