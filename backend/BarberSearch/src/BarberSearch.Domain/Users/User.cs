using System.Net.Mail;
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

    private readonly List<FavoriteBarber> _favoriteBarbers = [];

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;

    public string? PhoneNumber { get; private set; }
    public string? AvatarUrl { get; private set; }

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
        UserRole role = UserRole.Customer)
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
            SecurityStamp = NewSecurityStamp()
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
        => email?.Trim().ToLowerInvariant() ?? string.Empty;

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
                "First name cannot be empty.",
                nameof(firstName)));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            errors.Add(Error.Validation(
                "user.updateProfile.lastName",
                "Last name cannot be empty.",
                nameof(lastName)));
        }

        var avatarUrlError = UrlValidator.Validate(
            avatarUrl,
            "user.updateProfile.avatarUrl",
            nameof(avatarUrl),
            required: false,
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
        AvatarUrl = normalizedAvatarUrl;

        MarkUpdated();

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
                $"Unknown user role value {(int)role}.",
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
                "Barber profile identifier cannot be empty.",
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
                "Password hash cannot be empty.",
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

    /// <summary>
    /// Синтаксическая проверка канонического адреса.
    /// </summary>
    /// <remarks>
    /// <see cref="MailAddress.TryCreate(string, out MailAddress)"/> разбирает адрес по RFC,
    /// а не по регулярному выражению. Сравнение <c>Address</c> с исходной строкой отсекает
    /// форму с отображаемым именем (<c>Иван &lt;ivan@example.com&gt;</c>), которую он тоже
    /// принимает. Домен обязан содержать точку: <c>a@b</c> синтаксически допустим для
    /// <see cref="MailAddress"/>, но не является адресом публичного сервиса.
    /// </remarks>
    private static bool IsValidEmail(string email)
    {
        if (email.Length is < Constants.MIN_EMAIL_LENGTH or > Constants.MAX_EMAIL_LENGTH)
        {
            return false;
        }

        if (!MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.Ordinal))
        {
            return false;
        }

        var at = email.LastIndexOf('@');
        var dot = email.IndexOf('.', at + 1);

        return at > 0 &&
               dot > at + 1 &&
               dot < email.Length - 1 &&
               !email.Contains("..", StringComparison.Ordinal) &&
               !email.EndsWith('.');
    }

    private static void Validate(
        string email,
        string? passwordHash,
        string? firstName,
        string? lastName,
        UserRole role,
        List<Error> errors)
    {
        if (!IsValidEmail(email))
        {
            errors.Add(Error.Validation(
                "user.email",
                $"Email must be a valid address between {Constants.MIN_EMAIL_LENGTH} " +
                $"and {Constants.MAX_EMAIL_LENGTH} characters.",
                nameof(email)));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            errors.Add(Error.Validation(
                "user.passwordHash",
                "Password hash cannot be empty.",
                nameof(passwordHash)));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            errors.Add(Error.Validation(
                "user.firstName",
                "First name cannot be empty.",
                nameof(firstName)));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            errors.Add(Error.Validation(
                "user.lastName",
                "Last name cannot be empty.",
                nameof(lastName)));
        }

        if (!Enum.IsDefined(role))
        {
            errors.Add(Error.Validation(
                "user.role",
                $"Unknown user role value {(int)role}.",
                nameof(role)));
        }
    }
}
