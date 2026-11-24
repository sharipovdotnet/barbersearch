using BarberSearch.Domain.Common;
using BarberSearch.Domain.HairServices;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Specializations;

/// <summary>
/// Специализация из справочника — агрегатный корень. Связи барберов живут
/// в агрегате <c>BarberProfile</c> (сущность <c>BarberSpecialization</c>).
/// </summary>
/// <remarks>
/// У специализации ровно одна категория услуги (<see cref="ServiceCategory"/>): она
/// описывает направление, а категория услуги каталога берётся из категории её
/// специализации, поэтому отдельного поля у услуги нет.
/// </remarks>
public class Specialization : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>
    /// Категория услуг этого направления. Обязательна и меняется только
    /// <see cref="ChangeCategory"/>; смена разрешена всегда.
    /// </summary>
    public ServiceCategory Category { get; private set; }

    public bool IsActive { get; private set; }

    private Specialization()
    {
    }

    public static Result<Specialization, Failure> Create(
        string name,
        ServiceCategory category,
        string? description = null)
    {
        var errors = DomainErrors.Collect();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(Error.Validation(
                "specialization.name",
                DomainMessages.SPECIALIZATION_NAME_REQUIRED,
                nameof(name)));
        }

        ValidateCategory(category, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new Specialization
        {
            Name = name,
            Category = category,
            Description = description,
            IsActive = true
        };
    }

    /// <summary>
    /// Переименование и смена описания. Категория здесь не меняется — для неё
    /// отдельный метод <see cref="ChangeCategory"/>.
    /// </summary>
    public UnitResult<Failure> Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Failure(Error.Validation(
                "specialization.rename.name",
                DomainMessages.SPECIALIZATION_NAME_REQUIRED,
                nameof(name)));
        }

        Name = name;
        Description = description;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Меняет категорию направления. Разрешено всегда: услуги получают категорию
    /// из специализации, поэтому перенос направления в другую категорию не требует
    /// правки самих услуг.
    /// </summary>
    public UnitResult<Failure> ChangeCategory(ServiceCategory category)
    {
        var errors = DomainErrors.Collect();

        ValidateCategory(category, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        if (Category == category)
        {
            return DomainErrors.Ok();
        }

        Category = category;

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

    private static void ValidateCategory(ServiceCategory category, List<Error> errors)
    {
        if (!Enum.IsDefined(category))
        {
            errors.Add(Error.Validation(
                "specialization.category",
                DomainMessages.UnknownServiceCategory(category),
                nameof(category)));
        }
    }
}
