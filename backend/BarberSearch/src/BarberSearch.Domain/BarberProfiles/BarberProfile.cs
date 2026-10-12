using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberProfiles.ValueObjects;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.BarberSpecializations;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.WorkingSchedules;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.BarberProfiles;

public class BarberProfile : AggregateRoot
{
    private readonly List<BarberSalon> _salons = [];
    private readonly List<BarberService> _services = [];
    private readonly List<PortfolioPhoto> _portfolioPhotos = [];
    private readonly List<BarberSpecialization> _barberSpecializations = [];
    private readonly List<WorkingSchedule> _workingSchedules = [];

    private readonly List<PaymentMethod> _paymentMethods;
    private readonly List<Language> _languages = [];
    private readonly List<string> _certificatesAndTraining = [];
    private readonly List<string> _goodies = [];
    private readonly List<string> _achievements = [];
    private readonly List<string> _communicationFormat = [];

    public Guid UserId { get; private set; }

    public DisplayName DisplayName { get; private set; }

    public Grade Grade { get; private set; }

    // О себе - 1-2 предложения: о философии мастера, его опыте или фишке
    public string? Bio { get; private set; }

    public IReadOnlyCollection<Language> Languages
        => _languages.AsReadOnly();

    public YearsOfExperience YearsOfExperience { get; private set; }

    public IReadOnlyCollection<string> CertificatesAndTraining
        => _certificatesAndTraining.AsReadOnly();

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
    public string LatePolicy { get; private set; }

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

    public IReadOnlyCollection<WorkingSchedule> WorkingSchedules
        => _workingSchedules.AsReadOnly();

    private BarberProfile(
        Guid userId,
        DisplayName displayName,
        YearsOfExperience yearsOfExperience,
        string? bio,
        List<Language>? languages,
        List<string>? certificatesAndTraining,
        List<string>? achievements,
        List<PaymentMethod> paymentMethods,
        List<string>? communicationFormat,
        List<string>? goodies,
        string? latePolicy)
    {
        UserId = userId;
        DisplayName = displayName;
        YearsOfExperience = yearsOfExperience;
        Bio = bio;
        _languages = languages ?? [Language.Ru];
        _certificatesAndTraining = certificatesAndTraining ?? [];
        _achievements = achievements ?? [];
        _paymentMethods = paymentMethods;
        _communicationFormat = communicationFormat ?? [];
        _goodies = goodies ?? [];
        LatePolicy = latePolicy ?? string.Empty;
    }

    public static Result<BarberProfile, Failure> Create(BarberProfileCreateData createData)
    {
        ArgumentNullException.ThrowIfNull(createData);

        var validationResult = ValidateInput(createData.DisplayName, createData.YearsOfExperience);
        if (validationResult.IsFailure)
        {
            return validationResult.Error;
        }

        var (validDisplayName, validYears) = validationResult.Value;

        return new BarberProfile(
            createData.UserId,
            validDisplayName,
            validYears,
            createData.Bio,
            createData.Language,
            createData.CertificatesAndTraining,
            createData.Achievements,
            createData.PaymentMethods,
            createData.CommunicationFormat,
            createData.Goodies,
            createData.LatePolicy)
        {
            Grade = Grade.None,
            Rating = Constants.MIN_RATING,
            ReviewsCount = Constants.MIN_REVIEWS_COUNT,
            IsActive = true,
            IsVerified = false
        };
    }

    public UnitResult<Failure> UpdateProfile(BarberProfileUpdateData updateData)
    {
        ArgumentNullException.ThrowIfNull(updateData);

        var validationResult = ValidateInput(updateData.DisplayName, updateData.YearsOfExperience);
        if (validationResult.IsFailure)
        {
            return validationResult.Error;
        }

        var (validDisplayName, validYears) = validationResult.Value;

        DisplayName = validDisplayName;
        YearsOfExperience = validYears;
        Grade = updateData.Grade;
        Bio = updateData.Bio;

        _languages.AddRange(updateData.Language);
        _certificatesAndTraining.AddRange(updateData.CertificatesAndTraining);
        _achievements.AddRange(updateData.Achievements);
        _paymentMethods.AddRange(updateData.PaymentMethods);
        _communicationFormat.AddRange(updateData.CommunicationFormat);
        _goodies.AddRange(updateData.Goodies);
        
        LatePolicy = updateData.LatePolicy;

        MarkUpdated();

        return UnitResult.Success<Failure>();
    }

    private static Result<(DisplayName, YearsOfExperience), Failure> ValidateInput(
        string displayName, int yearsOfExperience)
    {
        var errors = new List<Error>();

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

    public UnitResult<Failure> UpdateRating(decimal rating, int reviewsCount)
    {
        if (rating < Constants.MIN_RATING ||
            rating > Constants.MAX_RATING ||
            reviewsCount < Constants.MIN_REVIEWS_COUNT)
        {
            return Error
                .Validation(
                    "barberProfile.updateRating.rating",
                    $"""
                     Rating must be between {Constants.MIN_RATING} and {Constants.MAX_RATING} 
                     and Reviews count must be non-negative.
                     """)
                .ToFailure();
        }

        Rating = Math.Round(rating, Constants.NUMBER_DECIMAL_PLACES);
        ReviewsCount = reviewsCount;

        MarkUpdated();

        return UnitResult.Success<Failure>();
    }

    public UnitResult<Failure> Verify()
        => ChangeState(IsVerified, v => IsVerified = v, true);

    public UnitResult<Failure> Activate()
        => ChangeState(IsActive, v => IsActive = v, true);

    public UnitResult<Failure> Deactivate()
        => ChangeState(IsActive, v => IsActive = v, false);

    private UnitResult<Failure> ChangeState(bool currentValue, Action<bool> setter, bool targetValue)
    {
        if (currentValue == targetValue)
        {
            return UnitResult.Success<Failure>();
        }

        setter(targetValue);
        MarkUpdated();

        return UnitResult.Success<Failure>();
    }


    public UnitResult<Failure> AddPhoto(PortfolioPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        if (_portfolioPhotos.Count >= Constants.MAX_PHOTOS_COUNT)
        {
            return Error
                .Conflict(
                    "barberProfile.addPhoto",
                    $"""
                     The number of photographs in the portfolio 
                     should not exceed {Constants.MAX_PHOTOS_COUNT} pieces.
                     """)
                .ToFailure();
        }

        if (_portfolioPhotos.Any(p => p.Id == photo.Id))
        {
            return Error
                .Conflict(
                    "barberProfile.addPhoto.duplicate",
                    "This photo is already in the portfolio.")
                .ToFailure();
        }

        _portfolioPhotos.Add(photo);
        MarkUpdated();

        return UnitResult.Success<Failure>();
    }

    public UnitResult<Failure> RemovePhoto(Guid photoId) =>
        RemoveEntity(
            _portfolioPhotos,
            photoId,
            "barberProfile.removePhoto.notFound",
            "The specified photo was not found in this artist's portfolio.");


    public UnitResult<Failure> AddSalon(BarberSalon salon) =>
        AddItem(
            salon,
            _salons,
            "barberProfile.addSalon",
            "This salon already exists.");

    public UnitResult<Failure> RemoveSalon(Guid salonId) =>
        RemoveEntity(
            _salons,
            salonId,
            "barberProfile.removeSalon.notFound",
            "The barber in the specified salon does not work.");


    public UnitResult<Failure> AddService(BarberService service) =>
        AddItem(
            service,
            _services,
            "barberProfile.addService",
            "This service already exists.");


    public UnitResult<Failure> RemoveService(Guid serviceId) =>
        RemoveEntity(
            _services,
            serviceId,
            "barberProfile.removeService.notFound",
            "The barber does not provide this service.");


    public UnitResult<Failure> AddSpecialization(BarberSpecialization specialization) =>
        AddItem(
            specialization,
            _barberSpecializations,
            "barberProfile.addSpecialization",
            "This specialization already exists.");

    public UnitResult<Failure> RemoveSpecialization(Guid specializationId) =>
        RemoveEntity(
            _barberSpecializations,
            specializationId,
            "barberProfile.removeSpecialization.notFound",
            "The barber specialization was not found.");


    public UnitResult<Failure> AddWorkingSchedule(WorkingSchedule schedule) =>
        AddItem(
            schedule,
            _workingSchedules,
            "barberProfile.addWorkingSchedule",
            "This working schedule already exists.");

    public UnitResult<Failure> RemoveWorkingSchedule(Guid scheduleId) =>
        RemoveEntity(
            _workingSchedules,
            scheduleId,
            "barberProfile.removeWorkingSchedule.notFound",
            "The barber working schedule was not found.");

    private UnitResult<Failure> RemoveEntity<T>(
        List<T> collection,
        Guid entityId,
        string errorCode,
        string errorMessage) where T : Entity
    {
        var removedCount = collection.RemoveAll(e => e.Id == entityId);

        if (removedCount == 0)
        {
            return Error
                .NotFound(errorCode, errorMessage, entityId)
                .ToFailure();
        }

        MarkUpdated();

        return UnitResult.Success<Failure>();
    }

    private UnitResult<Failure> AddItem<T>(
        T item,
        ICollection<T> collection,
        string errorCode,
        string errorMessage) where T : Entity
    {
        ArgumentNullException.ThrowIfNull(item);

        if (collection.Any(e => e.Id == item.Id))
        {
            return Error
                .Conflict(errorCode, errorMessage)
                .ToFailure();
        }

        collection.Add(item);
        MarkUpdated();

        return UnitResult.Success<Failure>();
    }
}