using BarberSearch.Domain.BarberProfiles.ValueObjects;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.BarberRequests;

/// <summary>
/// Заявка клиента на роль барбера — агрегатный корень.
/// </summary>
/// <remarks>
/// <para>
/// Заявка не меняет роль сама: <see cref="Approve"/> лишь фиксирует решение администратора,
/// а роль пользователя меняет Application в той же транзакции. Поэтому решить заявку
/// повторно нельзя — из <see cref="BarberRequestStatus.Pending"/> есть ровно один переход.
/// </para>
/// <para>
/// Поля заявки содержат персональные данные (город и текст «о себе»), поэтому сущность
/// сознательно не входит в allowlist <c>audit_log</c>: смена роли уже пишется там как
/// <c>user.updated</c>, а сами ответы заявителя журналу не нужны.
/// </para>
/// <para>
/// «Сейчас» приходит аргументом: дата подачи и дата решения — бизнес-метки, а не технические
/// штампы <see cref="Entity.CreatedAt"/>/<see cref="Entity.UpdatedAt"/>. Время выбирает
/// Application через <c>TimeProvider</c>, поэтому очередь заявок и решения проверяются
/// детерминированно (R-27).
/// </para>
/// </remarks>
public class BarberRequest : AggregateRoot
{
    /// <summary>
    /// Публичный код ошибки «решить можно только заявку в статусе <c>Pending</c>»; <c>409</c>.
    /// </summary>
    private const string REVIEW_INVALID_STATUS_CODE = "barberRequest.review.invalidStatus";

    /// <summary>
    /// Публичный код ошибки «причина отказа не передана»; <c>400</c>.
    /// </summary>
    private const string REJECTION_REASON_REQUIRED_CODE = "barberRequest.rejection.reasonRequired";

    /// <summary>
    /// Публичный код ошибки «причина отказа длиннее предела»; <c>400</c>.
    /// </summary>
    private const string REJECTION_REASON_TOO_LONG_CODE = "barberRequest.rejection.reasonTooLong";

    /// <summary>
    /// Заявитель. Ссылка на другой агрегат — только идентификатор, без навигации.
    /// </summary>
    public Guid UserId { get; private set; }

    public BarberRequestStatus Status { get; private set; }

    /// <summary>
    /// Имя, под которым заявитель хочет работать. Переиспользуется VO профиля барбера:
    /// правило 1..120 символов у заявки и у профиля одно и то же.
    /// </summary>
    public DisplayName DisplayName { get; private set; } = null!;

    /// <summary>
    /// Опыт работы. Переиспользуется VO профиля барбера: диапазон 1..70.
    /// </summary>
    public YearsOfExperience YearsOfExperience { get; private set; } = null!;

    /// <summary>
    /// Город, в котором заявитель работает или собирается работать.
    /// </summary>
    public string City { get; private set; } = null!;

    /// <summary>
    /// Свободный рассказ о себе; <c>null</c> — заявитель его не заполнил.
    /// </summary>
    public string? About { get; private set; }

    /// <summary>
    /// Уже работает в салоне. Признак информационный: на решение и на роль он не влияет.
    /// </summary>
    public bool HasSalon { get; private set; }

    /// <summary>
    /// Когда администратор принял решение; <c>null</c> — заявка ещё ждёт решения.
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>
    /// Кто принял решение; <c>null</c> — заявка ещё ждёт решения.
    /// </summary>
    public Guid? ReviewedByUserId { get; private set; }

    /// <summary>
    /// Причина отказа из <see cref="Reject"/>; <c>null</c> — заявка не отклонена.
    /// </summary>
    public string? RejectionReason { get; private set; }

    private BarberRequest()
    {
    }

    /// <summary>
    /// Создаёт заявку в статусе <see cref="BarberRequestStatus.Pending"/>.
    /// </summary>
    /// <remarks>
    /// Роль заявителя здесь не проверяется: домен не знает, кем является пользователь.
    /// «Подать заявку может только <c>Customer</c>» проверяет
    /// <c>CreateBarberRequestHandler</c> до вызова фабрики. Все поля валидируются сразу,
    /// поэтому клиент получает полный список проблем, а не первую из них.
    /// </remarks>
    /// <param name="userId">Заявитель.</param>
    /// <param name="displayName">
    /// Имя, под которым заявитель хочет работать; срезается от пробелов, 1..120 символов.
    /// </param>
    /// <param name="yearsOfExperience">Опыт работы; 1..70 лет.</param>
    /// <param name="city">Город; 1..100 символов.</param>
    /// <param name="about">Рассказ о себе; <c>null</c> или до 2000 символов.</param>
    /// <param name="hasSalon">Уже работает в салоне.</param>
    /// <param name="now">Момент подачи заявки в UTC.</param>
    public static Result<BarberRequest, Failure> Create(
        Guid userId,
        string displayName,
        int yearsOfExperience,
        string city,
        string? about,
        bool hasSalon,
        DateTimeOffset now)
    {
        var errors = DomainErrors.Collect();

        // VO не тримит значение: граница сама приводит имя к каноническому виду, как город
        // и «о себе» ниже. Иначе "  Имя  " и "Имя" стали бы разными значениями в ответе.
        var normalizedDisplayName = displayName?.Trim() ?? string.Empty;
        var displayNameResult = DisplayName.Create(normalizedDisplayName);
        if (displayNameResult.IsFailure)
        {
            errors.Add(displayNameResult.Error);
        }

        var yearsOfExperienceResult = YearsOfExperience.Create(yearsOfExperience);
        if (yearsOfExperienceResult.IsFailure)
        {
            errors.Add(yearsOfExperienceResult.Error);
        }

        var normalizedCity = city?.Trim() ?? string.Empty;

        if (normalizedCity.Length == 0)
        {
            errors.Add(Error.Validation(
                "barberRequest.city",
                "City cannot be empty.",
                nameof(city)));
        }
        else if (normalizedCity.Length > Constants.MAX_BARBER_REQUEST_CITY_LENGTH)
        {
            errors.Add(Error.Validation(
                "barberRequest.city.tooLong",
                $"City cannot be longer than {Constants.MAX_BARBER_REQUEST_CITY_LENGTH} characters.",
                nameof(city)));
        }

        var normalizedAbout = NormalizeAbout(about);

        if (normalizedAbout is { Length: > Constants.MAX_TEXT_LENGTH })
        {
            errors.Add(Error.Validation(
                "barberRequest.about.tooLong",
                $"About cannot be longer than {Constants.MAX_TEXT_LENGTH} characters.",
                nameof(about)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new BarberRequest
        {
            UserId = userId,
            Status = BarberRequestStatus.Pending,
            DisplayName = displayNameResult.Value,
            YearsOfExperience = yearsOfExperienceResult.Value,
            City = normalizedCity,
            About = normalizedAbout,
            HasSalon = hasSalon,
            // Дата подачи — бизнес-метка очереди (FIFO), поэтому берётся из Application,
            // а не из системных часов конструктора сущности.
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Одобряет заявку: фиксирует автора и момент решения.
    /// </summary>
    /// <remarks>
    /// Роль пользователя меняет Application в той же транзакции: домен заявки не знает
    /// про агрегат <c>User</c>. Повторное решение — <c>409 barberRequest.review.invalidStatus</c>.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> Approve(Guid adminId, DateTimeOffset now)
    {
        if (Status != BarberRequestStatus.Pending)
        {
            return ReviewInvalidStatus();
        }

        Status = BarberRequestStatus.Approved;
        ReviewedByUserId = adminId;
        ReviewedAt = now;
        RejectionReason = null;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отклоняет заявку с обязательной причиной.
    /// </summary>
    /// <remarks>
    /// Роль пользователя не меняется. Причина тримится и хранится без окружающих пробелов;
    /// пустая причина — <c>400 barberRequest.rejection.reasonRequired</c>, длиннее
    /// <see cref="Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH"/> символов —
    /// <c>400 barberRequest.rejection.reasonTooLong</c>.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="reason">Причина отказа.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> Reject(Guid adminId, string? reason, DateTimeOffset now)
    {
        if (Status != BarberRequestStatus.Pending)
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

        if (normalizedReason.Length > Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH)
        {
            return new Failure(Error.Validation(
                REJECTION_REASON_TOO_LONG_CODE,
                $"""
                 A rejection reason cannot be longer than
                 {Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH} characters.
                 """,
                nameof(reason)));
        }

        Status = BarberRequestStatus.Rejected;
        ReviewedByUserId = adminId;
        ReviewedAt = now;
        RejectionReason = normalizedReason;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Приводит рассказ о себе к каноническому виду: срезает пробелы и пустую строку
    /// хранит как <c>null</c>.
    /// </summary>
    private static string? NormalizeAbout(string? about)
    {
        var normalized = about?.Trim();

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Failure ReviewInvalidStatus()
        => new(Error.Conflict(
            REVIEW_INVALID_STATUS_CODE,
            "Only a pending barber request can be reviewed."));
}
