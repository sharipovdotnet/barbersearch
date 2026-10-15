using System;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.BarberServices;

/// <summary>
/// Услуга конкретного барбера: цена и длительность. Дочерняя сущность агрегата
/// <see cref="BarberProfile"/>: создаётся только методами агрегата.
/// </summary>
public class BarberService : Entity
{
    public Guid BarberProfileId { get; private set; }
    public Guid HairServiceId { get; private set; }

    public decimal Price { get; private set; }
    public int DurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    // Ссылки на HairService здесь нет — это граница другого агрегата.
    public BarberProfile BarberProfile { get; private set; } = null!;

    private BarberService()
    {
    }

    private BarberService(
        Guid barberProfileId,
        Guid hairServiceId,
        decimal price,
        int durationMinutes)
    {
        BarberProfileId = barberProfileId;
        HairServiceId = hairServiceId;
        Price = price;
        DurationMinutes = durationMinutes;

        IsActive = true;
    }

    /// <summary>
    /// Создаёт услугу от имени агрегата <c>BarberProfile</c>.
    /// </summary>
    internal static BarberService Create(
        Guid barberProfileId,
        Guid hairServiceId,
        decimal price,
        int durationMinutes)
        => new(barberProfileId, hairServiceId, price, durationMinutes);

    public UnitResult<Failure> UpdatePrice(decimal price)
    {
        if (DomainErrors.NegativePrice(price, "barberService.updatePrice") is { } error)
        {
            return DomainErrors.Fail(error);
        }

        Price = price;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> UpdateDuration(int durationMinutes)
    {
        if (DomainErrors.InvalidDuration(durationMinutes, "barberService.updateDuration") is { } error)
        {
            return DomainErrors.Fail(error);
        }

        DurationMinutes = durationMinutes;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Activate()
    {
        IsActive = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Deactivate()
    {
        IsActive = false;
        MarkUpdated();

        return DomainErrors.Ok();
    }
}
