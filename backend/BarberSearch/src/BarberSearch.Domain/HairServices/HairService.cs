using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.HairServices;

/// <summary>
/// Услуга из каталога — агрегатный корень. Цены и длительности конкретных барберов
/// живут в агрегате <c>BarberProfile</c> (сущность <c>BarberService</c>).
/// </summary>
/// <remarks>
/// Услуга принадлежит ровно одной специализации. Категории у услуги нет: категория —
/// свойство специализации, и витрина получает её через join, поэтому две одинаковые
/// категории в двух местах не расходятся. Ссылка на агрегат <c>Specialization</c> —
/// граница другого агрегата: навигации нет, только идентификатор, а существование
/// строки проверяет Application до вставки.
/// </remarks>
public class HairService : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>
    /// Специализация, к которой относится услуга. Обязательна: категория услуги
    /// вычисляется из категории специализации.
    /// </summary>
    public Guid SpecializationId { get; private set; }

    public int DefaultDurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    private HairService()
    {
    }

    public static Result<HairService, Failure> Create(
        string name,
        Guid specializationId,
        int defaultDurationMinutes,
        string? description = null)
    {
        var errors = DomainErrors.Collect();

        Validate(name, specializationId, defaultDurationMinutes, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new HairService
        {
            Name = name,
            SpecializationId = specializationId,
            DefaultDurationMinutes = defaultDurationMinutes,
            Description = description,
            IsActive = true
        };
    }

    /// <summary>
    /// Меняет название, описание, длительность и специализацию услуги.
    /// </summary>
    /// <remarks>
    /// Смена специализации разрешена всегда: категория услуги — это категория её
    /// специализации, отдельного запрета на перевод нет.
    /// </remarks>
    public UnitResult<Failure> Update(
        string name,
        string? description,
        int defaultDurationMinutes,
        Guid specializationId)
    {
        var errors = DomainErrors.Collect();

        Validate(name, specializationId, defaultDurationMinutes, errors);

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

        if (specializationId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                "hairService.specializationId",
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
