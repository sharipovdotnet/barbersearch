using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Salons;

/// <summary>
/// Салон — агрегатный корень. Барберы салона живут в агрегате <c>BarberProfile</c>
/// (сущность <c>BarberSalon</c>), поэтому коллекции барберов здесь нет.
/// </summary>
public class Salon : AggregateRoot
{
    public string Name { get; private set; } = null!;

    public string Address { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string? PostalCode { get; private set; }
    public string Country { get; private set; } = null!;

    /// <summary>
    /// Идентификатор часового пояса IANA (например, <c>Europe/Moscow</c>).
    /// Без него локальное время салона не перевести в UTC и слоты не посчитать.
    /// </summary>
    public string TimeZoneId { get; private set; } = null!;

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    public string? PhoneNumber { get; private set; }
    public string? WebsiteUrl { get; private set; }

    /// <summary>
    /// Виден ли салон в поиске и на карточках. Не то же самое, что
    /// <see cref="ModerationStatus"/>: салон участвует в публичной выдаче, только когда
    /// <c>IsActive</c> и статус модерации — <see cref="SalonModerationStatus.Active"/>.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Вид места приёма. Задаётся при создании и в этом срезе не меняется.
    /// </summary>
    /// <remarks>
    /// Тип не выводится из <see cref="IsListed"/>: кабинет одного мастера бывает и публичным,
    /// и скрытым. Для <see cref="PlaceKind.Home"/> и <see cref="PlaceKind.Mobile"/> адрес
    /// остаётся обязательным: у первого это адрес приёма, у второго — зона обслуживания.
    /// </remarks>
    public PlaceKind Kind { get; private set; }

    /// <summary>
    /// Показывается ли место в публичном каталоге салонов.
    /// </summary>
    /// <remarks>
    /// <c>false</c> — место не попадает в каталог и не требует модерации: оно видно только
    /// с карточки барбера, качество закрывает верификация профиля. Скрывать можно только
    /// <see cref="PlaceKind.SoloStudio"/>, <see cref="PlaceKind.Home"/> и
    /// <see cref="PlaceKind.Mobile"/>; барбершоп и студия аренды кресел обязаны быть в каталоге.
    /// Флаг задаётся только при создании, переходов в этом срезе нет.
    /// </remarks>
    public bool IsListed { get; private set; }

    /// <summary>
    /// Владелец салона — пользователь, создавший его сам как барбер; <c>null</c> — салон
    /// создан администратором. Это связь, а не роль: отдельной роли владельца нет.
    /// </summary>
    /// <remarks>
    /// Меняется только фабрикой <see cref="SubmitByOwner"/>: сеттера нет, передать владение
    /// другой учётной записи в этом срезе нельзя.
    /// </remarks>
    public Guid? OwnerUserId { get; private set; }

    /// <summary>
    /// Состояние проверки салона администратором. Управляет только модерация:
    /// владелец не может ни одобрить себя, ни изменить статус.
    /// </summary>
    public SalonModerationStatus ModerationStatus { get; private set; }

    /// <summary>
    /// Причина отказа из <see cref="Reject"/>; <c>null</c> — салон не отклонён.
    /// </summary>
    public string? RejectionReason { get; private set; }

    /// <summary>
    /// Когда администратор принял решение; <c>null</c> — салон ещё ждёт проверки
    /// (или создан администратором и не проходил её).
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>
    /// Кто принял решение; <c>null</c> — салон ещё ждёт проверки (или создан администратором).
    /// </summary>
    public Guid? ReviewedByUserId { get; private set; }

    private const string ADDRESS_ERROR_CODE = "salon.address";
    private const string CITY_ERROR_CODE = "salon.city";
    private const string COUNTRY_ERROR_CODE = "salon.country";
    private const string TIME_ZONE_ID_ERROR_CODE = "salon.timeZoneId";
    private const string WEBSITE_URL_ERROR_CODE = "salon.websiteUrl";
    private const string REJECTION_REASON_REQUIRED_CODE = "salon.reject.reasonRequired";
    private const string REJECTION_REASON_TOO_LONG_CODE = "salon.reject.reasonTooLong";
    private const string REVIEW_INVALID_STATUS_CODE = "salon.review.invalidStatus";
    private const string ACTIVATION_NOT_APPROVED_CODE = "salon.activation.notApproved";
    private const string KIND_UNKNOWN_CODE = "salon.kind.unknown";
    private const string LISTED_KIND_CODE = "salon.isListed.kind";

    private Salon()
    {
    }

    /// <summary>
    /// Создаёт салон администратором: владельца нет, статус — <see cref="SalonModerationStatus.Active"/>,
    /// салон сразу виден и участвует в поиске.
    /// </summary>
    /// <remarks>
    /// Путь барбера — <see cref="SubmitByOwner"/>: его салон ждёт проверки. Обе фабрики
    /// валидируют поля одинаково, поэтому правила адреса и координат не расходятся.
    /// <para>
    /// <see cref="IsListed"/> по умолчанию <c>true</c>: админский салон ведёт себя как раньше.
    /// Скрытое место (<c>isListed: false</c>) создаётся сразу активным, но в каталог не попадает:
    /// очередь модерации нужна только тому, что публикуется.
    /// </para>
    /// </remarks>
    /// <param name="kind">Вид места приёма; по умолчанию обычный барбершоп.</param>
    /// <param name="isListed">Показывать ли место в каталоге; по умолчанию <c>true</c>.</param>
    public static Result<Salon, Failure> Create(
        string name,
        string address,
        string city,
        string country,
        string timeZoneId,
        double latitude,
        double longitude,
        string? postalCode = null,
        string? phoneNumber = null,
        string? websiteUrl = null,
        PlaceKind kind = PlaceKind.Salon,
        bool isListed = true)
    {
        var salon = BuildValidated(
            name,
            address,
            city,
            country,
            timeZoneId,
            latitude,
            longitude,
            postalCode,
            phoneNumber,
            websiteUrl,
            kind,
            isListed);
        if (salon.IsFailure)
        {
            return salon.Error;
        }

        salon.Value.ModerationStatus = SalonModerationStatus.Active;
        salon.Value.IsActive = true;

        return salon;
    }

    /// <summary>
    /// Создаёт салон от имени барбера-владельца: статус <see cref="SalonModerationStatus.Pending"/>,
    /// в поиске салон не участвует до одобрения администратором.
    /// </summary>
    /// <remarks>
    /// Проверка «у пользователя есть профиль барбера» живёт в Application: домен салона
    /// не знает про профиль. Владелец фиксируется здесь и дальше не меняется.
    /// <para>
    /// Скрытое место (<paramref name="isListed"/> = <c>false</c>) не публикуется, поэтому
    /// модерация ему не нужна: оно сразу <see cref="SalonModerationStatus.Active"/> и
    /// <see cref="IsActive"/> = <c>true</c>, без решения администратора. Видно оно только
    /// с карточки барбера — контроль качества закрывает верификация профиля.
    /// </para>
    /// </remarks>
    /// <param name="ownerUserId">Пользователь-владелец (барбер с профилем).</param>
    /// <param name="kind">Вид места приёма; по умолчанию обычный барбершоп.</param>
    /// <param name="isListed">Показывать ли место в каталоге; по умолчанию <c>true</c>.</param>
    public static Result<Salon, Failure> SubmitByOwner(
        Guid ownerUserId,
        string name,
        string address,
        string city,
        string country,
        string timeZoneId,
        double latitude,
        double longitude,
        string? postalCode = null,
        string? phoneNumber = null,
        string? websiteUrl = null,
        PlaceKind kind = PlaceKind.Salon,
        bool isListed = true)
    {
        var salon = BuildValidated(
            name,
            address,
            city,
            country,
            timeZoneId,
            latitude,
            longitude,
            postalCode,
            phoneNumber,
            websiteUrl,
            kind,
            isListed);
        if (salon.IsFailure)
        {
            return salon.Error;
        }

        salon.Value.OwnerUserId = ownerUserId;

        if (isListed)
        {
            salon.Value.ModerationStatus = SalonModerationStatus.Pending;
            salon.Value.IsActive = false;
        }
        else
        {
            // Скрытое место не проходит модерацию: оно не попадает в каталог, значит
            // очередь проверки для него не нужна. ReviewedAt/ReviewedByUserId остаются null.
            salon.Value.ModerationStatus = SalonModerationStatus.Active;
            salon.Value.IsActive = true;
        }

        return salon;
    }

    private static Result<Salon, Failure> BuildValidated(
        string name,
        string address,
        string city,
        string country,
        string timeZoneId,
        double latitude,
        double longitude,
        string? postalCode,
        string? phoneNumber,
        string? websiteUrl,
        PlaceKind kind,
        bool isListed)
    {
        var errors = DomainErrors.Collect();

        ValidateRequired(name, nameof(name), "salon.name", errors);
        ValidateRequired(address, nameof(address), ADDRESS_ERROR_CODE, errors);
        ValidateRequired(city, nameof(city), CITY_ERROR_CODE, errors);
        ValidateRequired(country, nameof(country), COUNTRY_ERROR_CODE, errors);
        ValidateRequired(timeZoneId, nameof(timeZoneId), TIME_ZONE_ID_ERROR_CODE, errors);
        var normalizedTimeZoneId = ValidateTimeZone(timeZoneId, errors);
        ValidateCoordinates(latitude, longitude, errors);
        ValidatePlaceKind(kind, isListed, errors);

        var websiteUrlError = UrlValidator.Validate(
            websiteUrl,
            WEBSITE_URL_ERROR_CODE,
            nameof(websiteUrl),
            required: false,
            out var normalizedWebsiteUrl);
        if (websiteUrlError is not null)
        {
            errors.Add(websiteUrlError);
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new Salon
        {
            Name = name,
            Address = address,
            City = city,
            Country = country,
            TimeZoneId = normalizedTimeZoneId!,
            Latitude = latitude,
            Longitude = longitude,
            PostalCode = postalCode,
            PhoneNumber = phoneNumber,
            WebsiteUrl = normalizedWebsiteUrl,
            Kind = kind,
            IsListed = isListed
        };
    }

    /// <summary>
    /// Одобряет салон на модерации: салон становится активным и видимым.
    /// </summary>
    /// <remarks>
    /// Одобрить можно только салон в статусе <see cref="SalonModerationStatus.Pending"/>:
    /// повторное решение — <c>409 salon.review.invalidStatus</c>. Если администратор успел
    /// скрыть салон, одобрение всё равно включает видимость — это и есть решение.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> Approve(Guid adminId, DateTimeOffset now)
    {
        if (ModerationStatus != SalonModerationStatus.Pending)
        {
            return ReviewInvalidStatus();
        }

        ModerationStatus = SalonModerationStatus.Active;
        IsActive = true;
        ReviewedByUserId = adminId;
        ReviewedAt = now;
        RejectionReason = null;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отклоняет салон с обязательной причиной; салон остаётся невидимым.
    /// </summary>
    /// <remarks>
    /// Причина тримится и хранится без окружающих пробелов; пустая причина —
    /// <c>400 salon.reject.reasonRequired</c>, длиннее
    /// <see cref="Constants.MAX_SALON_REJECTION_REASON_LENGTH"/> символов —
    /// <c>400 salon.reject.reasonTooLong</c>. Отклонить можно только
    /// <see cref="SalonModerationStatus.Pending"/>-салон, повторное решение —
    /// <c>409 salon.review.invalidStatus</c>.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="reason">Причина отказа.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> Reject(Guid adminId, string? reason, DateTimeOffset now)
    {
        if (ModerationStatus != SalonModerationStatus.Pending)
        {
            return ReviewInvalidStatus();
        }

        var normalizedReason = reason?.Trim();

        if (string.IsNullOrEmpty(normalizedReason))
        {
            return new Failure(Error.Validation(
                REJECTION_REASON_REQUIRED_CODE,
                "A rejection reason is required.",
                nameof(reason)));
        }

        if (normalizedReason.Length > Constants.MAX_SALON_REJECTION_REASON_LENGTH)
        {
            return new Failure(Error.Validation(
                REJECTION_REASON_TOO_LONG_CODE,
                $"""
                 A rejection reason cannot be longer than
                 {Constants.MAX_SALON_REJECTION_REASON_LENGTH} characters.
                 """,
                nameof(reason)));
        }

        ModerationStatus = SalonModerationStatus.Rejected;
        IsActive = false;
        ReviewedByUserId = adminId;
        ReviewedAt = now;
        RejectionReason = normalizedReason;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> UpdateLocation(
        string address,
        string city,
        string country,
        string timeZoneId,
        double latitude,
        double longitude,
        string? postalCode = null)
    {
        var errors = DomainErrors.Collect();

        ValidateRequired(address, nameof(address), ADDRESS_ERROR_CODE, errors);
        ValidateRequired(city, nameof(city), CITY_ERROR_CODE, errors);
        ValidateRequired(country, nameof(country), COUNTRY_ERROR_CODE, errors);
        ValidateRequired(timeZoneId, nameof(timeZoneId), TIME_ZONE_ID_ERROR_CODE, errors);
        var normalizedTimeZoneId = ValidateTimeZone(timeZoneId, errors);
        ValidateCoordinates(latitude, longitude, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        Address = address;
        City = city;
        Country = country;
        TimeZoneId = normalizedTimeZoneId!;
        Latitude = latitude;
        Longitude = longitude;
        PostalCode = postalCode;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> UpdateContactInfo(
        string? phoneNumber,
        string? websiteUrl)
    {
        var errors = DomainErrors.Collect();

        var websiteUrlError = UrlValidator.Validate(
            websiteUrl,
            WEBSITE_URL_ERROR_CODE,
            nameof(websiteUrl),
            required: false,
            out var normalizedWebsiteUrl);
        if (websiteUrlError is not null)
        {
            errors.Add(websiteUrlError);
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        PhoneNumber = phoneNumber;
        WebsiteUrl = normalizedWebsiteUrl;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Включает видимость салона.
    /// </summary>
    /// <remarks>
    /// Включить можно только одобренный салон: pending/rejected сначала проходят модерацию,
    /// иначе барбер обходил бы проверку, просто включив строку. Отказ —
    /// <c>409 salon.activation.notApproved</c>.
    /// </remarks>
    public UnitResult<Failure> Activate()
    {
        if (ModerationStatus != SalonModerationStatus.Active)
        {
            return new Failure(Error.Conflict(
                ACTIVATION_NOT_APPROVED_CODE,
                "The salon must be approved before it can be activated."));
        }

        if (IsActive)
        {
            return DomainErrors.Ok();
        }

        IsActive = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Deactivate()
    {
        if (!IsActive)
        {
            return DomainErrors.Ok();
        }

        IsActive = false;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    private static void ValidateRequired(
        string? value,
        string field,
        string code,
        List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(Error.Validation(code, "Value cannot be empty.", field));
        }
    }

    /// <summary>
    /// Проверяет, что часовой пояс разрешается в системе, и возвращает его нормализованное
    /// значение: пробелы по краям срезаются, Windows-идентификатор переводится в IANA.
    /// </summary>
    /// <remarks>
    /// Без этой проверки опечатка в админском запросе сохранялась бы в БД и ломала
    /// расчёт слотов уже на чтении (R-01). <c>null</c> означает, что пояс пуст или
    /// не разрешается; пустое значение отдельно помечает <see cref="ValidateRequired"/>.
    /// </remarks>
    private static string? ValidateTimeZone(string? timeZoneId, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        var candidate = timeZoneId.Trim();

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId))
        {
            candidate = ianaId;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out _))
        {
            return candidate;
        }

        errors.Add(Error.Validation(
            "salon.timeZoneId.unknown",
            $"Time zone '{timeZoneId}' cannot be resolved.",
            nameof(timeZoneId)));

        return null;
    }

    /// <summary>
    /// Проверяет тип места и совместимость с видимостью в каталоге.
    /// </summary>
    /// <remarks>
    /// Неизвестное число в перечислении — <c>salon.kind.unknown</c> даже тогда, когда оно
    /// пришло в обход JSON (числа в JSON для enum'ов запрещены, R-23). Скрывать можно только
    /// кабинет, дом и выезд; барбершоп и студия аренды кресел публичны по своей природе.
    /// При неизвестном типе правило видимости не проверяется: одна ошибка на поле, а не две.
    /// </remarks>
    private static void ValidatePlaceKind(
        PlaceKind kind,
        bool isListed,
        List<Error> errors)
    {
        if (!Enum.IsDefined(kind))
        {
            errors.Add(Error.Validation(
                KIND_UNKNOWN_CODE,
                $"Unknown place kind value {(int)kind}.",
                nameof(kind)));

            return;
        }

        if (isListed && kind is PlaceKind.Home or PlaceKind.Mobile)
        {
            errors.Add(Error.Validation(
                LISTED_KIND_CODE,
                "A home or mobile place cannot be listed in the catalogue.",
                nameof(isListed)));
        }

        if (!isListed && kind is PlaceKind.Salon or PlaceKind.RentalStudio)
        {
            errors.Add(Error.Validation(
                LISTED_KIND_CODE,
                "A salon or rental studio must be listed in the catalogue.",
                nameof(isListed)));
        }
    }

    private static void ValidateCoordinates(
        double latitude,
        double longitude,
        List<Error> errors)
    {
        if (latitude is < Constants.MIN_LATITUDE or > Constants.MAX_LATITUDE)
        {
            errors.Add(Error.Validation(
                "salon.latitude",
                $"Latitude must be between {Constants.MIN_LATITUDE} and {Constants.MAX_LATITUDE}, got {latitude}.",
                nameof(latitude)));
        }

        if (longitude is < Constants.MIN_LONGITUDE or > Constants.MAX_LONGITUDE)
        {
            errors.Add(Error.Validation(
                "salon.longitude",
                $"""
                 Longitude must be between {Constants.MIN_LONGITUDE} and {Constants.MAX_LONGITUDE}, 
                 got {longitude}.
                 """,
                nameof(longitude)));
        }
    }

    private static Failure ReviewInvalidStatus()
        => new(Error.Conflict(
            REVIEW_INVALID_STATUS_CODE,
            "Only a pending salon can be reviewed."));
}
