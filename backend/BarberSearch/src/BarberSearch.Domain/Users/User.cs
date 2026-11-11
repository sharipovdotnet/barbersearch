using System.Net;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.FavoriteBarbers;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Users;

/// <summary>
/// Пользователь — агрегатный корень. Владеет списком избранных барберов.
/// Профиль барбера — отдельный агрегат, здесь только его идентификатор.
/// </summary>
public class User : AggregateRoot
{
    /// <summary>
    /// Предохранитель счётчика неудач: больше этого значения он не растёт, поэтому показатель
    /// степени в <see cref="LockoutDelay"/> остаётся маленьким и <see cref="int"/> не переполняется.
    /// </summary>
    private const int FAILED_LOGIN_ATTEMPTS_CAP = Constants.MAX_FAILED_LOGIN_ATTEMPTS + 10;

    /// <summary>
    /// Показатель степени, после которого задержка заведомо упирается в
    /// <see cref="Constants.LOGIN_LOCKOUT_MAX_MINUTES"/>: 2^6 = 64 минуты при базе в 1 минуту.
    /// </summary>
    private const int MAX_LOCKOUT_EXPONENT = 6;

    /// <summary>
    /// Текст ошибки «имя пользователя пусто».
    /// </summary>
    private const string FIRST_NAME_REQUIRED = "First name cannot be empty.";

    /// <summary>
    /// Текст ошибки «фамилия пользователя пуста».
    /// </summary>
    private const string LAST_NAME_REQUIRED = "Last name cannot be empty.";

    /// <summary>
    /// Текст ошибки «хеш пароля пуст».
    /// </summary>
    private const string PASSWORD_HASH_REQUIRED = "Password hash cannot be empty.";

    private const string AVATAR_MODERATION_NO_AVATAR_CODE = "user.avatarModeration.noAvatar";
    private const string AVATAR_MODERATION_INVALID_STATUS_CODE = "user.avatarModeration.invalidStatus";
    private const string AVATAR_MODERATION_REASON_REQUIRED_CODE = "user.avatarModeration.reasonRequired";
    private const string AVATAR_MODERATION_REASON_TOO_LONG_CODE = "user.avatarModeration.reasonTooLong";

    private readonly List<FavoriteBarber> _favoriteBarbers = [];

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;

    public string? PhoneNumber { get; private set; }
    public string? AvatarUrl { get; private set; }

    /// <summary>
    /// Скрыт ли аватар администратором.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Флаг живёт на пользователе, а не на отдельной строке файла: у аккаунта один аватар
    /// и отдельной таблицы для него нет. Скрытие — это маска на чтении, а не удаление:
    /// <see cref="AvatarUrl"/> и файл остаются на месте, поэтому «Вернуть фото» отдаёт
    /// тот же кадр, а публичные чтения получают <c>null</c>.
    /// </para>
    /// <para>
    /// Маска не заменяет <see cref="AvatarModerationStatus"/>: публичный кадр требует и
    /// <see cref="PhotoModerationStatus.Approved"/>, и отсутствия блокировки. Реальная смена
    /// <see cref="AvatarUrl"/> снимает флаг, потому что это новый файл, — но новый кадр
    /// теперь ждёт проверки. Повторная запись того же URL флаг сохраняет: иначе сохранение
    /// имени в кабинете разблокировало бы скрытое фото.
    /// </para>
    /// </remarks>
    public bool IsAvatarBlocked { get; private set; }

    /// <summary>
    /// Допуск аватара к публикации: решение администратора о самом кадре.
    /// </summary>
    /// <remarks>
    /// Значение по умолчанию — <see cref="PhotoModerationStatus.Approved"/>: у аккаунта
    /// без аватара проверять нечего, а существующие кадры миграция не перепроверяет.
    /// Реальная загрузка ставит <see cref="PhotoModerationStatus.Pending"/>. Публичный
    /// кадр — это <see cref="PhotoModerationStatus.Approved"/> и
    /// <see cref="IsAvatarBlocked"/> = <c>false</c>.
    /// </remarks>
    public PhotoModerationStatus AvatarModerationStatus { get; private set; }
        = PhotoModerationStatus.Approved;

    /// <summary>
    /// Техническая метка поступления кадра в очередь проверки; <c>null</c> — аватара нет.
    /// </summary>
    /// <remarks>
    /// Пишется системными часами, как <see cref="Entity.CreatedAt"/>: значение не участвует
    /// в бизнес-инвариантах, а решение администратора приходит параметром. По этой метке
    /// очередь строится в порядке FIFO.
    /// </remarks>
    public DateTimeOffset? AvatarSubmittedAt { get; private set; }

    /// <summary>
    /// Когда администратор принял решение по аватару; <c>null</c> — кадр ещё ждёт проверки.
    /// </summary>
    public DateTimeOffset? AvatarReviewedAt { get; private set; }

    /// <summary>
    /// Кто принял решение по аватару; <c>null</c> — кадр ещё ждёт проверки.
    /// </summary>
    public Guid? AvatarReviewedByUserId { get; private set; }

    /// <summary>
    /// Причина отказа из <see cref="RejectAvatar"/>; <c>null</c> — кадр не отклонён.
    /// </summary>
    public string? AvatarRejectionReason { get; private set; }

    public bool IsEmailVerified { get; private set; }
    public bool IsPhoneVerified { get; private set; }

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Отпечаток состояния учётных данных. Попадает в claims access-токена и cookie,
    /// поэтому смена пароля, роли или деактивация делают уже выданные токены недействительными.
    /// </summary>
    public string SecurityStamp { get; private set; } = null!;

    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>
    /// Сколько неудачных попыток входа подряд зафиксировано у аккаунта.
    /// </summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// Момент последней неудачной попытки. По нему решается, начинать счёт заново
    /// (пауза больше <see cref="Constants.LOGIN_FAILURE_RESET_MINUTES"/> минут) или продолжать.
    /// </summary>
    public DateTimeOffset? LastFailedLoginAt { get; private set; }

    /// <summary>
    /// Конец текущей блокировки входа. <c>null</c> — блокировки нет; прошедшее значение
    /// блокировкой уже не является (<see cref="IsLockedOut"/>).
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; private set; }

    /// <summary>
    /// Идентификатор профиля барбера, если пользователь в роли барбера.
    /// Навигации на <c>BarberProfile</c> нет — это граница другого агрегата.
    /// </summary>
    public Guid? BarberProfileId { get; private set; }

    /// <summary>
    /// Редакция Политики, на которую дано согласие при регистрации; <c>null</c> — записи нет.
    /// </summary>
    /// <remarks>
    /// Записи нет у аккаунтов, созданных до этапа #42 и администратором: согласие нельзя
    /// доказать задним числом, поэтому backfill не делается, а UI честно говорит «Записи нет».
    /// </remarks>
    public string? ConsentPolicyVersion { get; private set; }

    /// <summary>
    /// Момент согласия в UTC; <c>null</c> — записи нет.
    /// </summary>
    public DateTimeOffset? ConsentedAt { get; private set; }

    /// <summary>
    /// IP-адрес, с которого дано согласие; <c>null</c> — записи нет либо адрес неизвестен.
    /// </summary>
    /// <remarks>
    /// Это персональные данные: поле не входит в API-ответы, в allowlist аудита и в логи.
    /// </remarks>
    public IPAddress? ConsentIp { get; private set; }

    public IReadOnlyCollection<FavoriteBarber> FavoriteBarbers
        => _favoriteBarbers.AsReadOnly();

    private User()
    {
    }

    public static Result<User, Failure> Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        UserRole role = UserRole.Customer,
        UserConsent? consent = null)
    {
        var errors = DomainErrors.Collect();
        var normalizedEmail = NormalizeEmail(email);

        Validate(normalizedEmail, passwordHash, firstName, lastName, role, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new User
        {
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            IsActive = true,
            SecurityStamp = NewSecurityStamp(),
            ConsentPolicyVersion = consent?.PolicyVersion,
            ConsentedAt = consent?.ConsentedAt,
            ConsentIp = consent?.IpAddress
        };
    }

    /// <summary>
    /// Приводит адрес к каноническому виду: срезает пробелы по краям и переводит в нижний регистр.
    /// </summary>
    /// <remarks>
    /// Канонический вид — часть контракта хранения: по нему строится уникальный индекс,
    /// и в нём адрес сравнивается при входе. Без нормализации <c>User@x</c> и <c>user@x</c>
    /// становились двумя пользователями, а аккаунт с пробелом по краям нельзя было открыть
    /// для входа (R-03).
    /// </remarks>
    public static string NormalizeEmail(string? email)
        => EmailAddress.Normalize(email);

    public UnitResult<Failure> UpdateProfile(
        string firstName,
        string lastName,
        string? phoneNumber,
        string? avatarUrl)
    {
        var errors = DomainErrors.Collect();

        if (string.IsNullOrWhiteSpace(firstName))
        {
            errors.Add(Error.Validation(
                "user.updateProfile.firstName",
                FIRST_NAME_REQUIRED,
                nameof(firstName)));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            errors.Add(Error.Validation(
                "user.updateProfile.lastName",
                LAST_NAME_REQUIRED,
                nameof(lastName)));
        }

        var avatarUrlError = ValidateAvatarUrl(
            avatarUrl,
            "user.updateProfile.avatarUrl",
            out var normalizedAvatarUrl);
        if (avatarUrlError is not null)
        {
            errors.Add(avatarUrlError);
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;

        // Та же логика смены аватара, что у SetAvatar: сохранение имени с текущим URL
        // не трогает ни статус проверки, ни блокировку, а новый кадр уходит в Pending.
        ApplyAvatarChange(normalizedAvatarUrl);

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Устанавливает или снимает аватар пользователя.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Принимаются три вида значения: <c>null</c> или пустая строка — аватар снимается;
    /// <b>собственный</b> media-URL (разобранный <see cref="UserAvatarMediaUrl.TryParse"/> с
    /// идентификатором этого пользователя) — сохраняется как есть; абсолютный <c>http(s)</c>
    /// URL — легаси-путь администратора, который исторически умеет внешнюю ссылку, — проходит
    /// через <see cref="UrlValidator"/>. Любое другое значение отклоняется
    /// <c>400 user.setAvatar.avatarUrl</c> с <see cref="Error.InvalidField"/> = <c>avatarUrl</c>:
    /// относительный путь чужого файла не должен попасть в <c>img src</c>.
    /// </para>
    /// <para>
    /// Байты изображения домен не проверяет — это делает <c>ImageFileInspector</c> в
    /// Application до сохранения файла. <see cref="Entity.UpdatedAt"/> меняется только при
    /// реальном изменении значения: повторная запись того же URL не создаёт правку.
    /// </para>
    /// <para>
    /// Реальная смена значения переводит кадр в <see cref="PhotoModerationStatus.Pending"/>
    /// и снимает <see cref="IsAvatarBlocked"/>: это новый файл, который ещё не публиковался.
    /// Публично новый кадр появится только после <see cref="ApproveAvatar"/>. <c>null</c>
    /// означает удаление: аватара нет, скрывать и проверять нечего. Повторная запись того же
    /// URL (ранний возврат ниже) не трогает ни статус, ни флаг.
    /// </para>
    /// </remarks>
    /// <param name="avatarUrl">Новый URL аватара либо <c>null</c>/пустая строка.</param>
    public UnitResult<Failure> SetAvatar(string? avatarUrl)
    {
        var avatarUrlError = ValidateAvatarUrl(
            avatarUrl,
            "user.setAvatar.avatarUrl",
            out var normalizedAvatarUrl);
        if (avatarUrlError is not null)
        {
            return new Failure(avatarUrlError);
        }

        // Повторная запись того же значения — не изменение: UpdatedAt не трогаем,
        // статус проверки и административную блокировку не меняем.
        if (!ApplyAvatarChange(normalizedAvatarUrl))
        {
            return DomainErrors.Ok();
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Скрывает аватар администратором: публичные чтения отдают <c>null</c>, а раздача файла
    /// отвечает <c>404</c> всем, кроме администратора.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Скрывать нечего, если аватар не загружен, — это конфликт
    /// <c>user.blockAvatar.noAvatar</c>, а не успех: действие администратора ни на что
    /// не влияет. Повторная блокировка идемпотентна и не меняет
    /// <see cref="Entity.UpdatedAt"/>: иначе аудит писал бы строку без изменения флага.
    /// </para>
    /// <para>
    /// Ни <see cref="IsActive"/>, ни <see cref="Role"/>, ни <see cref="SecurityStamp"/>
    /// не меняются: это модерация контента, а не блокировка доступа.
    /// </para>
    /// </remarks>
    public UnitResult<Failure> BlockAvatar()
    {
        if (AvatarUrl is null)
        {
            return new Failure(Error.Conflict(
                "user.blockAvatar.noAvatar",
                "The user has no avatar to block."));
        }

        if (IsAvatarBlocked)
        {
            return DomainErrors.Ok();
        }

        IsAvatarBlocked = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Возвращает скрытый аватар: флаг снимается, а URL и файл не менялись и остаются прежними.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: повторный вызов — успех без <see cref="Entity.UpdatedAt"/> и без строки
    /// аудита. Наличие URL не проверяется: скрывать без аватара нельзя
    /// (<see cref="BlockAvatar"/>), а удаление аватара снимает флаг само, поэтому
    /// заблокированный аккаунт без URL невозможен.
    /// </remarks>
    public UnitResult<Failure> UnblockAvatar()
    {
        if (!IsAvatarBlocked)
        {
            return DomainErrors.Ok();
        }

        IsAvatarBlocked = false;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Одобряет аватар: кадр допускается к публикации, если он не заблокирован.
    /// </summary>
    /// <remarks>
    /// Проверки идут в порядке контракта: сначала «аватара нет» —
    /// <c>409 user.avatarModeration.noAvatar</c> (проверять нечего), затем «уже не
    /// <see cref="PhotoModerationStatus.Pending"/>» — <c>409 user.avatarModeration.invalidStatus</c>.
    /// Повторное решение — ошибка, а не успех. Причина отклонения очищается.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> ApproveAvatar(Guid adminId, DateTimeOffset now)
    {
        if (AvatarUrl is null)
        {
            return AvatarModerationNoAvatar();
        }

        if (AvatarModerationStatus != PhotoModerationStatus.Pending)
        {
            return AvatarModerationInvalidStatus();
        }

        AvatarModerationStatus = PhotoModerationStatus.Approved;
        AvatarRejectionReason = null;
        AvatarReviewedByUserId = adminId;
        AvatarReviewedAt = now;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отклоняет аватар с обязательной причиной: публично кадр не показывается.
    /// </summary>
    /// <remarks>
    /// Проверки те же, что у <see cref="ApproveAvatar"/>: нет URL —
    /// <c>409 user.avatarModeration.noAvatar</c>, кадр уже не
    /// <see cref="PhotoModerationStatus.Pending"/> — <c>409 user.avatarModeration.invalidStatus</c>.
    /// Причина тримится, обязательна и ограничена
    /// <see cref="Constants.MAX_PHOTO_REJECTION_REASON_LENGTH"/> символами:
    /// <c>400 user.avatarModeration.reasonRequired</c> /
    /// <c>400 user.avatarModeration.reasonTooLong</c> с
    /// <see cref="Error.InvalidField"/> = <c>reason</c>.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="reason">Причина отказа.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> RejectAvatar(Guid adminId, string? reason, DateTimeOffset now)
    {
        if (AvatarUrl is null)
        {
            return AvatarModerationNoAvatar();
        }

        if (AvatarModerationStatus != PhotoModerationStatus.Pending)
        {
            return AvatarModerationInvalidStatus();
        }

        var normalizedReason = reason?.Trim();

        if (string.IsNullOrEmpty(normalizedReason))
        {
            return new Failure(Error.Validation(
                AVATAR_MODERATION_REASON_REQUIRED_CODE,
                "A rejection reason is required.",
                nameof(reason)));
        }

        if (normalizedReason.Length > Constants.MAX_PHOTO_REJECTION_REASON_LENGTH)
        {
            return new Failure(Error.Validation(
                AVATAR_MODERATION_REASON_TOO_LONG_CODE,
                $"""
                 A rejection reason cannot be longer than
                 {Constants.MAX_PHOTO_REJECTION_REASON_LENGTH} characters.
                 """,
                nameof(reason)));
        }

        AvatarModerationStatus = PhotoModerationStatus.Rejected;
        AvatarRejectionReason = normalizedReason;
        AvatarReviewedByUserId = adminId;
        AvatarReviewedAt = now;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Применяет реальную смену аватара — общая логика <see cref="SetAvatar"/> и
    /// <see cref="UpdateProfile"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Сравнение с текущим значением — <see cref="StringComparison.Ordinal"/> и до
    /// присваивания: повторная запись того же URL (кабинет отправляет текущий
    /// <c>avatarUrl</c> обратно в <c>PUT users/{id}/profile</c>) не меняет ни статус, ни
    /// блокировку, ни метки времени. Один приватный метод на две операции, чтобы правила
    /// не разошлись.
    /// </para>
    /// <para>
    /// Новый кадр — это премодерация: <see cref="PhotoModerationStatus.Pending"/>,
    /// техническая метка <see cref="AvatarSubmittedAt"/> и очистка прошлого решения.
    /// Административная блокировка снимается: новый файл ещё не публиковался. <c>null</c>
    /// (удаление) — проверять нечего: <see cref="PhotoModerationStatus.Approved"/>
    /// и пустые метки, как у аккаунта без аватара.
    /// </para>
    /// </remarks>
    /// <param name="normalizedAvatarUrl">Нормализованный URL или <c>null</c>.</param>
    /// <returns><c>true</c>, если значение действительно изменилось.</returns>
    private bool ApplyAvatarChange(string? normalizedAvatarUrl)
    {
        if (string.Equals(AvatarUrl, normalizedAvatarUrl, StringComparison.Ordinal))
        {
            return false;
        }

        AvatarUrl = normalizedAvatarUrl;
        IsAvatarBlocked = false;
        AvatarReviewedAt = null;
        AvatarReviewedByUserId = null;
        AvatarRejectionReason = null;

        if (normalizedAvatarUrl is null)
        {
            AvatarModerationStatus = PhotoModerationStatus.Approved;
            AvatarSubmittedAt = null;
        }
        else
        {
            AvatarModerationStatus = PhotoModerationStatus.Pending;

            // Техническая метка поступления в очередь, как Entity.CreatedAt: бизнес-«сейчас»
            // домен не читает, решение администратора приходит параметром.
            AvatarSubmittedAt = DateTimeOffset.UtcNow;
        }

        return true;
    }

    /// <summary>
    /// Проверяет аватар по общим для <see cref="SetAvatar"/> и <see cref="UpdateProfile"/>
    /// правилам и возвращает нормализованное значение.
    /// </summary>
    /// <remarks>
    /// Один приватный метод на две операции: кабинет отправляет текущий <c>avatarUrl</c> обратно
    /// в <c>PUT users/{id}/profile</c>, и сохранение имени не должно падать на собственном
    /// media-URL. Код ошибки передаётся вызывающим: у методов он разный и является частью
    /// HTTP-контракта. Абсолютный URL проверяет <see cref="UrlValidator"/> — его правила
    /// (схема, хост, <c>userinfo</c>, длина) здесь не ослабляются.
    /// </remarks>
    /// <param name="avatarUrl">Проверяемое значение.</param>
    /// <param name="code">Код ошибки для недопустимого значения.</param>
    /// <param name="normalized">
    /// Обрезанное значение при успехе; <c>null</c> для пустого значения и при ошибке.
    /// </param>
    private Error? ValidateAvatarUrl(string? avatarUrl, string code, out string? normalized)
    {
        normalized = null;

        var candidate = avatarUrl?.Trim();

        if (string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        // Свой загруженный файл: root-relative URL, собранный UserAvatarMediaUrl.Build.
        if (UserAvatarMediaUrl.TryParse(candidate, Id, out _, out _))
        {
            normalized = candidate;

            return null;
        }

        // Внешняя ссылка (легаси-путь администратора) — по общим правилам UrlValidator.
        return UrlValidator.Validate(
            candidate,
            code,
            nameof(avatarUrl),
            required: false,
            out normalized);
    }

    /// <summary>
    /// Меняет адрес электронной почты после подтверждения владения новым адресом.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Один доменный шаг: адрес нормализуется и проверяется, затем помечается подтверждённым
    /// (<see cref="IsEmailVerified"/> остаётся мягким флагом — вход он не блокирует), а отпечаток
    /// учётных данных ротируется, поэтому все выданные cookie и access-токены перестают
    /// проходить проверку. Отзыв refresh-токенов — задача Application: у домена нет доступа
    /// к чужому агрегату.
    /// </para>
    /// <para>
    /// Синтаксис проверяется до сравнения с текущим адресом: кривой ввод не должен получать
    /// ответ «адрес совпадает». При ошибке адрес, флаг и stamp не меняются.
    /// </para>
    /// </remarks>
    /// <param name="email">Новый адрес; нормализуется внутри.</param>
    public UnitResult<Failure> ChangeEmail(string email)
    {
        var normalized = EmailAddress.Normalize(email);

        var emailError = EmailAddress.Validate(
            normalized,
            "user.changeEmail.newEmail",
            "newEmail");
        if (emailError is not null)
        {
            return new Failure(emailError);
        }

        if (string.Equals(Email, normalized, StringComparison.Ordinal))
        {
            return new Failure(Error.Validation(
                "user.changeEmail.sameEmail",
                "The new email is the same as the current one.",
                "newEmail"));
        }

        Email = normalized;
        IsEmailVerified = true;

        // Ротация stamp — единственный механизм, который гасит уже выданные cookie и
        // access-токены; без неё старая сессия пережила бы смену адреса (AGENT.md §17.10).
        RotateSecurityStamp();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> MarkEmailAsVerified()
    {
        if (IsEmailVerified)
        {
            return DomainErrors.Ok();
        }

        IsEmailVerified = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Помечает телефон подтверждённым.
    /// </summary>
    public UnitResult<Failure> MarkPhoneAsVerified()
    {
        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            return new Failure(Error.Validation(
                "user.markPhoneAsVerified.phoneNumber",
                "A phone number must be set before it can be verified."));
        }

        if (IsPhoneVerified)
        {
            return DomainErrors.Ok();
        }

        IsPhoneVerified = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Меняет роль пользователя.
    /// </summary>
    public UnitResult<Failure> ChangeRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            return new Failure(Error.Validation(
                "user.changeRole",
                UnknownRole(role),
                nameof(role)));
        }

        Role = role;
        RotateSecurityStamp();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Привязывает профиль барбера к пользователю.
    /// </summary>
    public UnitResult<Failure> LinkBarberProfile(Guid barberProfileId)
    {
        if (barberProfileId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                "user.linkBarberProfile",
                DomainMessages.BARBER_PROFILE_ID_REQUIRED,
                nameof(barberProfileId)));
        }

        BarberProfileId = barberProfileId;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Добавляет барбера в избранное. Повторное добавление — конфликт.
    /// Правило «нельзя добавить в избранное себя» проверяется в Application:
    /// домен не знает, какой профиль барбера принадлежит этому пользователю.
    /// </summary>
    public UnitResult<Failure> AddFavorite(Guid barberProfileId)
    {
        if (_favoriteBarbers.Any(f => f.BarberProfileId == barberProfileId))
        {
            return new Failure(Error.Conflict(
                "user.addFavorite.duplicate",
                "This barber is already in the user's favorites."));
        }

        _favoriteBarbers.Add(FavoriteBarber.Create(Id, barberProfileId));
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Убирает барбера из избранного.
    /// </summary>
    public UnitResult<Failure> RemoveFavorite(Guid barberProfileId)
    {
        var removed = _favoriteBarbers.RemoveAll(f => f.BarberProfileId == barberProfileId);

        if (removed == 0)
        {
            return new Failure(Error.NotFound(
                "user.removeFavorite.notFound",
                "This barber is not in the user's favorites.",
                barberProfileId));
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отмечает вход пользователя: ставит <see cref="LastLoginAt"/> и обновляет
    /// <see cref="Entity.UpdatedAt"/>.
    /// </summary>
    /// <remarks>
    /// Время берётся системными часами: это техническая метка аудита, а не бизнес-«сейчас».
    /// Домен сознательно не знает про <c>TimeProvider</c> (политика R-27, <c>AGENT.md</c> §7.6);
    /// управляемое время живёт в Application/Web, где принимаются зависящие от времени решения.
    /// </remarks>
    public UnitResult<Failure> RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Сообщает, заблокирован ли вход на момент <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// Сравнение строгое: в момент <see cref="LockoutEnd"/> блокировка уже закончилась.
    /// Иначе клиент, дождавшийся конца задержки, получал бы отказ ещё раз.
    /// </remarks>
    public bool IsLockedOut(DateTimeOffset now)
        => LockoutEnd is { } lockoutEnd && lockoutEnd > now;

    /// <summary>
    /// Регистрирует неудачную попытку входа и, начиная с
    /// <see cref="Constants.MAX_FAILED_LOGIN_ATTEMPTS"/>-й подряд, блокирует вход.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Метод не может провалиться: это фиксация факта, а не бизнес-проверка. Решение «вход
    /// заблокирован» принимает вызывающий через <see cref="IsLockedOut"/>.
    /// </para>
    /// <para>
    /// Пауза больше <see cref="Constants.LOGIN_FAILURE_RESET_MINUTES"/> минут после предыдущей
    /// неудачи начинает счёт заново. Задержка после порога растёт как
    /// <c>LOGIN_LOCKOUT_BASE_MINUTES * 2^(attempts - MAX_FAILED_LOGIN_ATTEMPTS)</c> и ограничена
    /// <see cref="Constants.LOGIN_LOCKOUT_MAX_MINUTES"/>; счётчик тоже ограничен сверху, чтобы
    /// степень двойки не переполнила <see cref="int"/>.
    /// </para>
    /// </remarks>
    public UnitResult<Failure> RegisterFailedLogin(DateTimeOffset now)
    {
        if (LastFailedLoginAt is { } lastFailedAt &&
            now - lastFailedAt > TimeSpan.FromMinutes(Constants.LOGIN_FAILURE_RESET_MINUTES))
        {
            FailedLoginAttempts = 0;
            LockoutEnd = null;
        }

        FailedLoginAttempts = Math.Min(FailedLoginAttempts + 1, FAILED_LOGIN_ATTEMPTS_CAP);
        LastFailedLoginAt = now;

        if (FailedLoginAttempts >= Constants.MAX_FAILED_LOGIN_ATTEMPTS)
        {
            LockoutEnd = now + LockoutDelay(FailedLoginAttempts);
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Сбрасывает счётчик неудач и снимает блокировку — вызывается после успешного входа.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: если сбрасывать нечего, <see cref="Entity.UpdatedAt"/> не меняется.
    /// </remarks>
    public UnitResult<Failure> ResetFailedLogin()
    {
        if (FailedLoginAttempts == 0 && LastFailedLoginAt is null && LockoutEnd is null)
        {
            return DomainErrors.Ok();
        }

        FailedLoginAttempts = 0;
        LastFailedLoginAt = null;
        LockoutEnd = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Заменяет хеш пароля. Открытый пароль в домен не попадает: хеширование — задача слоя Web.
    /// </summary>
    /// <remarks>
    /// Вызывается при смене пароля и при перехешировании, когда <c>IPasswordHasher</c>
    /// сообщает, что формат хеша устарел.
    /// </remarks>
    public UnitResult<Failure> ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return new Failure(Error.Validation(
                "user.changePasswordHash",
                PASSWORD_HASH_REQUIRED,
                nameof(passwordHash)));
        }

        PasswordHash = passwordHash;
        RotateSecurityStamp();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Activate()
    {
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
        RotateSecurityStamp();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Меняет отпечаток учётных данных. Уже выданные access-токен и cookie перестают проходить
    /// проверку: claim <c>security_stamp</c> в них больше не совпадает с сохранённым значением.
    /// </summary>
    /// <remarks>
    /// Метод не может провалиться и не возвращает результат: новое значение всегда непустое,
    /// а проверка токена живёт в слое Web.
    /// </remarks>
    public void RotateSecurityStamp()
    {
        SecurityStamp = NewSecurityStamp();
        MarkUpdated();
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Задержка после <paramref name="attempts"/>-й неудачи подряд: 1, 2, 4, … минут,
    /// но не больше <see cref="Constants.LOGIN_LOCKOUT_MAX_MINUTES"/>.
    /// </summary>
    private static TimeSpan LockoutDelay(int attempts)
    {
        var exponent = Math.Min(
            attempts - Constants.MAX_FAILED_LOGIN_ATTEMPTS,
            MAX_LOCKOUT_EXPONENT);

        var minutes = Math.Min(
            (long)Constants.LOGIN_LOCKOUT_BASE_MINUTES << exponent,
            Constants.LOGIN_LOCKOUT_MAX_MINUTES);

        return TimeSpan.FromMinutes(minutes);
    }

    private static void Validate(
        string email,
        string? passwordHash,
        string? firstName,
        string? lastName,
        UserRole role,
        List<Error> errors)
    {
        var emailError = EmailAddress.Validate(email, "user.email", nameof(email));
        if (emailError is not null)
        {
            errors.Add(emailError);
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            errors.Add(Error.Validation(
                "user.passwordHash",
                PASSWORD_HASH_REQUIRED,
                nameof(passwordHash)));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            errors.Add(Error.Validation(
                "user.firstName",
                FIRST_NAME_REQUIRED,
                nameof(firstName)));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            errors.Add(Error.Validation(
                "user.lastName",
                LAST_NAME_REQUIRED,
                nameof(lastName)));
        }

        if (!Enum.IsDefined(role))
        {
            errors.Add(Error.Validation(
                "user.role",
                UnknownRole(role),
                nameof(role)));
        }
    }

    /// <summary>
    /// Текст ошибки «значение роли не определено в <see cref="UserRole"/>».
    /// </summary>
    /// <param name="role">Некорректная роль.</param>
    private static string UnknownRole(UserRole role)
        => $"Unknown user role value {(int)role}.";

    private static Failure AvatarModerationNoAvatar()
        => new(Error.Conflict(
            AVATAR_MODERATION_NO_AVATAR_CODE,
            "The user has no avatar to review."));

    private static Failure AvatarModerationInvalidStatus()
        => new(Error.Conflict(
            AVATAR_MODERATION_INVALID_STATUS_CODE,
            "Only a pending avatar can be reviewed."));
}
