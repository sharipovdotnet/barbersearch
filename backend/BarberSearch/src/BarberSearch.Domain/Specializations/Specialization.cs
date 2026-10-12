using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Specializations;

/// <summary>
/// Специализация из справочника — агрегатный корень. Связи барберов живут
/// в агрегате <c>BarberProfile</c> (сущность <c>BarberSpecialization</c>).
/// </summary>
public class Specialization : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    private Specialization()
    {
    }

    public static Result<Specialization, Failure> Create(
        string name,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Failure(Error.Validation(
                "specialization.name",
                "Specialization name cannot be empty.",
                nameof(name)));
        }

        return new Specialization
        {
            Name = name,
            Description = description,
            IsActive = true
        };
    }

    /// <summary>
    /// Переименование и смена описания.
    /// </summary>
    public UnitResult<Failure> Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Failure(Error.Validation(
                "specialization.rename.name",
                "Specialization name cannot be empty.",
                nameof(name)));
        }

        Name = name;
        Description = description;

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
}
