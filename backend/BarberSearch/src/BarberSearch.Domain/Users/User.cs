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

        Validate(email, passwordHash, firstName, lastName, role, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new User
        {
            Email = email,
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            IsActive = true,
            SecurityStamp = NewSecurityStamp()
        };
    }

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

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        AvatarUrl = avatarUrl;

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

    public UnitResult<Failure> RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
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

    private static void Validate(
        string? email,
        string? passwordHash,
        string? firstName,
        string? lastName,
        UserRole role,
        List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            errors.Add(Error.Validation(
                "user.email",
                "Email must be a non-empty address.",
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
