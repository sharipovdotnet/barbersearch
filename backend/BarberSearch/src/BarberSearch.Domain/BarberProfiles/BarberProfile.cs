using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberProfiles.ValueObjects;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.BarberSpecializations;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.TimeOffs;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.BarberProfiles;

/// <summary>
/// Профиль барбера — агрегатный корень. Владеет салонами (вместе с расписанием),
/// услугами, специализациями, портфолио и отпусками.
/// </summary>
public class BarberProfile : AggregateRoot
{
    private readonly List<BarberSalon> _salons = [];
    private readonly List<BarberService> _services = [];
    private readonly List<PortfolioPhoto> _portfolioPhotos = [];
    private readonly List<BarberSpecialization> _barberSpecializations = [];
    private readonly List<TimeOff> _timeOffs = [];

    private List<PaymentMethod> _paymentMethods = [];
    private List<Language> _languages = [];
    private List<string> _certificatesAndTraining = [];
    private List<string> _goodies = [];
    private List<string> _achievements = [];
    private List<string> _communicationFormat = [];

    public Guid UserId { get; private set; }

    public DisplayName DisplayName { get; private set; } = null!;

    public Grade Grade { get; private set; }

    // О себе - 1-2 предложения: о философии мастера, его опыте или фишке
    public string? Bio { get; private set; }

    public IReadOnlyCollection<Language> Languages
        => _languages.AsReadOnly();

    public YearsOfExperience YearsOfExperience { get; private set; } = null!;

    public IReadOnlyCollection<string> CertificatesAndTraining
        => _certificatesAndTraining.AsReadOnly();

    /// <summary>
    /// Средний рейтинг барбера в шкале 1.00..5.00 — той же, что и <c>Review.Rating</c>.
    /// <c>null</c> означает «отзывов ещё нет»; значение и <see cref="ReviewsCount"/> меняются только вместе.
    /// </summary>
    public decimal? Rating { get; private set; }

    public int ReviewsCount { get; private set; }

    // Достижения: «Топ-10 в районе», «1000+ стрижек», «Быстрая запись», «Прошел верификацию»
    public IReadOnlyCollection<string> Achievements
        => _achievements.AsReadOnly();

    // Способы оплаты: Наличные, Карта, Перевод по номеру телефона, Оплата в приложении
    public IReadOnlyCollection<PaymentMethod> PaymentMethods
        => _paymentMethods.AsReadOnly();

    // Формат общения: «Люблю поболтать», «Могу помолчать (Silent mode)», «Обсуждаем футбол/игры»
    public IReadOnlyCollection<string> CommunicationFormat
        => _communicationFormat.AsReadOnly();

    // Комфорт в кресле: «Предлагаю кофе/виски», «Есть PlayStation / Netflix во время стрижки»
    public IReadOnlyCollection<string> Goodies
        => _goodies.AsReadOnly();

    // Отношение к опозданиям, например, «Жду максимум 10 минут»
    public string LatePolicy { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }
    public bool IsVerified { get; private set; }

    public IReadOnlyCollection<BarberSalon> Salons
        => _salons.AsReadOnly();

    public IReadOnlyCollection<BarberService> Services
        => _services.AsReadOnly();

    public IReadOnlyCollection<PortfolioPhoto> PortfolioPhotos
        => _portfolioPhotos.AsReadOnly();

    public IReadOnlyCollection<BarberSpecialization> BarberSpecializations
        => _barberSpecializations.AsReadOnly();

    public IReadOnlyCollection<TimeOff> TimeOffs
        => _timeOffs.AsReadOnly();

    /// <summary>
    /// Пустой конструктор для материализации EF Core (и для прокси).
    /// </summary>
    private BarberProfile()
    {
    }

    /// <summary>
    /// Создаёт профиль барбера. Данные приходят значениями, а не записью: запись-контракт
    /// (<c>BarberProfileCreateRequest</c>) живёт в слое Contracts, на который домен не ссылается.
    /// </summary>
    public static Result<BarberProfile, Failure> Create(
        Guid userId,
        string displayName,
        int yearsOfExperience,
        string? bio,
        List<Language>? languages,
        List<string>? certificatesAndTraining,
        List<string>? achievements,
        List<PaymentMethod>? paymentMethods,
        List<string>? communicationFormat,
        List<string>? goodies,
        string? latePolicy)
    {
        var inputResult = ValidateInput(displayName, yearsOfExperience);
        if (inputResult.IsFailure)
        {
            return inputResult.Error;
        }

        var (validDisplayName, validYearsOfExperience) = inputResult.Value;

        return new BarberProfile
        {
            UserId = userId,
            DisplayName = validDisplayName,
            YearsOfExperience = validYearsOfExperience,
            Bio = bio,
            _languages = languages ?? [Language.Ru],
            _certificatesAndTraining = certificatesAndTraining ?? [],
            _achievements = achievements ?? [],
            _paymentMethods = paymentMethods ?? [],
            _communicationFormat = communicationFormat ?? [],
            _goodies = goodies ?? [],
            LatePolicy = latePolicy ?? string.Empty,
            Grade = Grade.None,
            // null = отзывов ещё нет. Ноль в этом поле больше не пишется: это второе
            // представление «нет отзывов» в одном и том же поле.
            Rating = null,
            ReviewsCount = Constants.MIN_REVIEWS_COUNT,
            IsActive = true,
            IsVerified = false
        };
    }

    /// <summary>
    /// Обновляет редактируемые барбером поля. Коллекции заменяются целиком, а не дополняются.
    /// Грейд здесь не меняется — см. <see cref="ChangeGrade"/>.
    /// </summary>
    public UnitResult<Failure> UpdateProfile(
        string displayName,
        int yearsOfExperience,
        string? bio,
        List<Language>? languages,
        List<string>? certificatesAndTraining,
        List<string>? achievements,
        List<PaymentMethod>? paymentMethods,
        List<string>? communicationFormat,
        List<string>? goodies,
        string? latePolicy)
    {
        var inputResult = ValidateInput(displayName, yearsOfExperience);
        if (inputResult.IsFailure)
        {
            return inputResult.Error;
        }

        var (validDisplayName, validYearsOfExperience) = inputResult.Value;

        DisplayName = validDisplayName;
        YearsOfExperience = validYearsOfExperience;
        Bio = bio;
        LatePolicy = latePolicy ?? string.Empty;

        Replace(_languages, languages);
        Replace(_certificatesAndTraining, certificatesAndTraining);
        Replace(_achievements, achievements);
        Replace(_paymentMethods, paymentMethods);
        Replace(_communicationFormat, communicationFormat);
        Replace(_goodies, goodies);

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Меняет грейд барбера. Отдельный метод, потому что это административное действие,
    /// а не часть обычного редактирования профиля.
    /// </summary>
    public UnitResult<Failure> ChangeGrade(Grade grade)
    {
        if (!Enum.IsDefined(grade))
        {
            return new Failure(Error.Validation(
                "barberProfile.changeGrade",
                $"Unknown barber grade value {(int)grade}.",
                nameof(grade)));
        }

        Grade = grade;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Пересчитывает рейтинг по отзывам. <paramref name="rating"/> = <c>null</c> вместе с
    /// <paramref name="reviewsCount"/> = 0 означает «отзывов нет».
    /// </summary>
    public UnitResult<Failure> UpdateRating(decimal? rating, int reviewsCount)
    {
        if (reviewsCount < Constants.MIN_REVIEWS_COUNT)
        {
            return new Failure(Error.Validation(
                "barberProfile.updateRating.reviewsCount",
                "Reviews count cannot be negative.",
                nameof(reviewsCount)));
        }

        if (reviewsCount == 0)
        {
            Rating = null;
            ReviewsCount = 0;
            MarkUpdated();

            return DomainErrors.Ok();
        }

        if (rating is null ||
            rating < Constants.MIN_RATING ||
            rating > Constants.MAX_RATING)
        {
            return new Failure(Error.Validation(
                "barberProfile.updateRating.rating",
                $"""
                 Rating must be between {Constants.MIN_RATING} and {Constants.MAX_RATING} 
                 when there are reviews (reviews count = {reviewsCount}).
                 """,
                nameof(rating)));
        }

        Rating = Math.Round(rating.Value, Constants.NUMBER_DECIMAL_PLACES);
        ReviewsCount = reviewsCount;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Подтверждает профиль. Неактивного барбера верифицировать нельзя.
    /// </summary>
    public UnitResult<Failure> Verify()
    {
        if (IsVerified)
        {
            return DomainErrors.Ok();
        }

        if (!IsActive)
        {
            return new Failure(Error.Conflict(
                "barberProfile.verify.inactive",
                "An inactive barber profile cannot be verified."));
        }

        IsVerified = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Снимает верификацию.
    /// </summary>
    public UnitResult<Failure> Unverify()
    {
        if (!IsVerified)
        {
            return DomainErrors.Ok();
        }

        IsVerified = false;
        MarkUpdated();

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
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Добавляет фотографию в портфолио. Агрегат сам создаёт ребёнка, поэтому
    /// прикрепить фотографию чужого барбера невозможно.
    /// </summary>
    public UnitResult<Failure> AddPhoto(
        string url,
        string? thumbnailUrl = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new Failure(Error.Validation(
                "barberProfile.addPhoto.url",
                "Photo URL cannot be empty.",
                nameof(url)));
        }

        if (_portfolioPhotos.Count >= Constants.MAX_PHOTOS_COUNT)
        {
            return new Failure(Error.Conflict(
                "barberProfile.addPhoto.limit",
                $"The number of photographs in the portfolio should not exceed {Constants.MAX_PHOTOS_COUNT} pieces."));
        }

        _portfolioPhotos.Add(PortfolioPhoto.Create(Id, url, thumbnailUrl, description));
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> RemovePhoto(Guid photoId)
        => RemoveChild(
            _portfolioPhotos,
            photoId,
            "barberProfile.removePhoto.notFound",
            "The specified photo was not found in this artist's portfolio.");

    /// <summary>
    /// Меняет порядок фотографии в портфолио.
    /// </summary>
    public UnitResult<Failure> ReorderPhoto(Guid photoId, int sortOrder)
    {
        var photo = _portfolioPhotos.FirstOrDefault(p => p.Id == photoId);

        if (photo is null)
        {
            return new Failure(Error.NotFound(
                "barberProfile.reorderPhoto.notFound",
                "The specified photo was not found in this artist's portfolio.",
                photoId));
        }

        photo.Reorder(sortOrder);
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Добавляет салон. Дубликат определяется по <c>SalonId</c>, а не по <c>Id</c> записи.
    /// </summary>
    public UnitResult<Failure> AddSalon(Guid salonId, bool isPrimary = false)
    {
        if (_salons.Any(s => s.SalonId == salonId))
        {
            return new Failure(Error.Conflict(
                "barberProfile.addSalon.duplicate",
                "This salon already exists in the barber profile."));
        }

        if (isPrimary)
        {
            foreach (var existing in _salons)
            {
                existing.SetAsSecondary();
            }
        }

        _salons.Add(BarberSalon.Create(Id, salonId, isPrimary));
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> RemoveSalon(Guid salonId)
    {
        var removed = _salons.RemoveAll(s => s.SalonId == salonId);

        if (removed == 0)
        {
            return new Failure(Error.NotFound(
                "barberProfile.removeSalon.notFound",
                "The barber in the specified salon does not work.",
                salonId));
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Назначает основной салон. У профиля одновременно может быть только один основной салон.
    /// </summary>
    public UnitResult<Failure> SetPrimarySalon(Guid salonId)
    {
        if (_salons.All(s => s.SalonId != salonId))
        {
            return new Failure(Error.NotFound(
                "barberProfile.setPrimarySalon.notFound",
                "The barber in the specified salon does not work.",
                salonId));
        }

        foreach (var salon in _salons)
        {
            if (salon.SalonId == salonId)
            {
                salon.SetAsPrimary();
            }
            else
            {
                salon.SetAsSecondary();
            }
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Находит салон барбера по идентификатору салона — точка входа для работы с расписанием.
    /// </summary>
    public Result<BarberSalon, Failure> FindSalon(Guid salonId)
    {
        var salon = _salons.FirstOrDefault(s => s.SalonId == salonId);

        if (salon is null)
        {
            return new Failure(Error.NotFound(
                "barberProfile.findSalon.notFound",
                "The barber in the specified salon does not work.",
                salonId));
        }

        return salon;
    }

    /// <summary>
    /// Добавляет услугу барбера. Дубликат определяется по <c>HairServiceId</c>.
    /// </summary>
    public UnitResult<Failure> AddService(
        Guid hairServiceId,
        decimal price,
        int durationMinutes)
    {
        var errors = DomainErrors.Collect();

        if (price < 0)
        {
            errors.Add(Error.Validation(
                "barberProfile.addService.price",
                "Price cannot be negative.",
                nameof(price)));
        }

        if (durationMinutes < Constants.MIN_DURATION_MINUTES ||
            durationMinutes > Constants.MAX_DURATION_MINUTES)
        {
            errors.Add(Error.Validation(
                "barberProfile.addService.duration",
                $"""
                 Duration must be between {Constants.MIN_DURATION_MINUTES} 
                 and {Constants.MAX_DURATION_MINUTES} minutes.
                 """,
                nameof(durationMinutes)));
        }

        if (_services.Any(s => s.HairServiceId == hairServiceId))
        {
            errors.Add(Error.Conflict(
                "barberProfile.addService.duplicate",
                "This service already exists in the barber profile."));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        _services.Add(BarberService.Create(Id, hairServiceId, price, durationMinutes));
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> RemoveService(Guid hairServiceId)
    {
        var removed = _services.RemoveAll(s => s.HairServiceId == hairServiceId);

        if (removed == 0)
        {
            return new Failure(Error.NotFound(
                "barberProfile.removeService.notFound",
                "The barber does not provide this service.",
                hairServiceId));
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Добавляет специализацию. Дубликат определяется по <c>SpecializationId</c>.
    /// </summary>
    public UnitResult<Failure> AddSpecialization(Guid specializationId)
    {
        if (_barberSpecializations.Any(s => s.SpecializationId == specializationId))
        {
            return new Failure(Error.Conflict(
                "barberProfile.addSpecialization.duplicate",
                "This specialization already exists in the barber profile."));
        }

        _barberSpecializations.Add(BarberSpecialization.Create(Id, specializationId));
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> RemoveSpecialization(Guid specializationId)
    {
        var removed = _barberSpecializations.RemoveAll(
            s => s.SpecializationId == specializationId);

        if (removed == 0)
        {
            return new Failure(Error.NotFound(
                "barberProfile.removeSpecialization.notFound",
                "The barber specialization was not found.",
                specializationId));
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Добавляет нерабочий период (отпуск). Интервалы отпусков не пересекаются.
    /// </summary>
    public UnitResult<Failure> AddTimeOff(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? reason = null)
    {
        if (endAt <= startAt)
        {
            return new Failure(Error.Validation(
                "barberProfile.addTimeOff.period",
                "Time off end must be after its start.",
                nameof(endAt)));
        }

        if (_timeOffs.Any(t => t.Overlaps(startAt, endAt)))
        {
            return new Failure(Error.Conflict(
                "barberProfile.addTimeOff.overlap",
                "This time off period overlaps with an existing one."));
        }

        _timeOffs.Add(TimeOff.Create(Id, startAt, endAt, reason));
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> RemoveTimeOff(Guid timeOffId)
        => RemoveChild(
            _timeOffs,
            timeOffId,
            "barberProfile.removeTimeOff.notFound",
            "The barber time off was not found.");

    private static Result<(DisplayName, YearsOfExperience), Failure> ValidateInput(
        string displayName,
        int yearsOfExperience)
    {
        var errors = DomainErrors.Collect();

        var displayNameResult = DisplayName.Create(displayName);
        if (displayNameResult.IsFailure)
        {
            errors.Add(displayNameResult.Error);
        }

        var yearsOfExperienceResult = YearsOfExperience.Create(yearsOfExperience);
        if (yearsOfExperienceResult.IsFailure)
        {
            errors.Add(yearsOfExperienceResult.Error);
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return (displayNameResult.Value, yearsOfExperienceResult.Value);
    }

    /// <summary>
    /// Полная замена содержимого коллекции: <c>Clear()</c> перед <c>AddRange()</c>.
    /// </summary>
    private static void Replace<T>(List<T> target, IEnumerable<T>? source)
    {
        target.Clear();

        if (source is not null)
        {
            target.AddRange(source);
        }
    }

    private UnitResult<Failure> RemoveChild<T>(
        List<T> collection,
        Guid entityId,
        string errorCode,
        string errorMessage) where T : Entity
    {
        var removed = collection.RemoveAll(e => e.Id == entityId);

        if (removed == 0)
        {
            return new Failure(Error.NotFound(errorCode, errorMessage, entityId));
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }
}
