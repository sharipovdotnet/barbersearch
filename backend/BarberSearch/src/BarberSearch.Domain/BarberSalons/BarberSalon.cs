using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.BarberSalons;

public class BarberSalon : Entity
{
    public Guid BarberProfileId { get; private set; }
    public Guid SalonId { get; private set; }

    public bool IsPrimary { get; private set; }

    public BarberProfile BarberProfile { get; private set; } = null!;
    public Salon Salon { get; private set; } = null!;

    private BarberSalon(
        Guid barberProfileId,
        Guid salonId,
        bool isPrimary = false)
    {
        BarberProfileId = barberProfileId;
        SalonId = salonId;
        IsPrimary = isPrimary;
    }

    public static Result<BarberSalon, Error> Create(
        Guid barberProfileId,
        Guid salonId,
        bool isPrimary = false)
    {
        return new BarberSalon(barberProfileId, salonId, isPrimary);
    }

    public void SetAsPrimary()
    {
        IsPrimary = true;
        MarkUpdated();
    }

    public void SetAsSecondary()
    {
        IsPrimary = false;
        MarkUpdated();
    }
}