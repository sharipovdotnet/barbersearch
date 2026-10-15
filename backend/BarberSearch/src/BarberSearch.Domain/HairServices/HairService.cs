using System;
using System.Collections.Generic;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.HairServices;

/// <summary>
/// Услуга из каталога — агрегатный корень. Цены и длительности конкретных барберов
/// живут в агрегате <c>BarberProfile</c> (сущность <c>BarberService</c>).
/// </summary>
public class HairService : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public ServiceCategory Category { get; private set; }

    public int DefaultDurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    private HairService()
    {
    }

    public static Result<HairService, Failure> Create(
        string name,
        ServiceCategory category,
        int defaultDurationMinutes,
        string? description = null)
    {
        var errors = DomainErrors.Collect();

        Validate(name, category, defaultDurationMinutes, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new HairService
        {
            Name = name,
            Category = category,
            DefaultDurationMinutes = defaultDurationMinutes,
            Description = description,
            IsActive = true
        };
    }

    public UnitResult<Failure> Update(
        string name,
        string? description,
        int defaultDurationMinutes)
    {
        var errors = DomainErrors.Collect();

        Validate(name, Category, defaultDurationMinutes, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        Name = name;
        Description = description;
        DefaultDurationMinutes = defaultDurationMinutes;

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
                $"Unknown service category value {(int)category}.",
                nameof(category)));
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
