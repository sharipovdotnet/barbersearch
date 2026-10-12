using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.HairServices;

namespace BarberSearch.Domain.BarberServices;

public class BarberService : Entity
{
    public Guid BarberProfileId { get; private set; }
    public Guid HairServiceId { get; private set; }

    public decimal Price { get; private set; }
    public int DurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public BarberProfile BarberProfile { get; private set; } = null!;
    public HairService HairService { get; private set; } = null!;

    private BarberService()
    {
    }

    public BarberService(
        Guid barberProfileId,
        Guid hairServiceId,
        decimal price,
        int durationMinutes)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price));

        if (durationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));

        BarberProfileId = barberProfileId;
        HairServiceId = hairServiceId;
        Price = price;
        DurationMinutes = durationMinutes;

        IsActive = true;
    }

    public void UpdatePrice(decimal price)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price));

        Price = price;
        MarkUpdated();
    }

    public void UpdateDuration(int durationMinutes)
    {
        if (durationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));

        DurationMinutes = durationMinutes;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }
}