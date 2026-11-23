using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.HairServices;

/// <summary>
/// Услуга из каталога — агрегатный корень. Цены и длительности конкретных барберов
/// живут в агрегате <c>BarberProfile</c> (сущность <c>BarberService</c>).
/// </summary>
/// <remarks>
/// Услуга принадлежит ровно одному направлению — <see cref="SpecializationId"/>.
/// Ссылка на агрегат <c>Specialization</c> — это граница другого агрегата: навигации нет,
/// только идентификатор, а существование строки проверяет Application до вставки.
/// </remarks>
public class HairService : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public ServiceCategory Category { get; private set; }

    /// <summary>
    /// Направление (специализация), к которому относится услуга. Обязательно:
    /// на витрине клиент сначала выбирает направление, затем услугу этого направления.
    /// </summary>
    public Guid SpecializationId { get; private set; }

    public int DefaultDurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    private HairService()
    {
    }

    public static Result<HairService, Failure> Create(
        string name,
        ServiceCategory category,
        Guid specializationId,
        int defaultDurationMinutes,
        string? description = null)
    {
        var errors = DomainErrors.Collect();

        Validate(name, category, specializationId, defaultDurationMinutes, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new HairService
        {
            Name = name,
            Category = category,
            SpecializationId = specializationId,
            DefaultDurationMinutes = defaultDurationMinutes,
            Description = description,
            IsActive = true
        };
    }

    /// <summary>
    /// Меняет название, описание, длительность и направление услуги.
    /// </summary>
    /// <remarks>
    /// Смена направления допустима только администратором и только когда ни один барбер
    /// не предлагает услугу без нового направления: проверка идёт в Application, потому что
    /// требует чтения связей других агрегатов.
    /// </remarks>
    public UnitResult<Failure> Update(
        string name,
        string? description,
        int defaultDurationMinutes,
        Guid specializationId)
    {
        var errors = DomainErrors.Collect();

        Validate(name, Category, specializationId, defaultDurationMinutes, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        Name = name;
        Description = description;
        DefaultDurationMinutes = defaultDurationMinutes;
        SpecializationId = specializationId;

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

    private static void Validate(
        string? name,
        ServiceCategory category,
        Guid specializationId,
        int defaultDurationMinutes,
        List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(Error.Validation(
                "hairService.name",
                "Hair service name cannot be empty.",
                nameof(name)));
        }

        if (!Enum.IsDefined(category))
        {
            errors.Add(Error.Validation(
                "hairService.category",
                DomainMessages.UnknownServiceCategory(category),
                nameof(category)));
        }

        if (specializationId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                "hairService.specialization",
                DomainMessages.HAIR_SERVICE_SPECIALIZATION_REQUIRED,
                nameof(specializationId)));
        }

        if (defaultDurationMinutes < Constants.MIN_DURATION_MINUTES ||
            defaultDurationMinutes > Constants.MAX_DURATION_MINUTES)
        {
            errors.Add(Error.Validation(
                "hairService.defaultDurationMinutes",
                $"""
                 Default duration must be between {Constants.MIN_DURATION_MINUTES} 
                 and {Constants.MAX_DURATION_MINUTES} minutes, got {defaultDurationMinutes}.
                 """,
                nameof(defaultDurationMinutes)));
        }
    }
}
