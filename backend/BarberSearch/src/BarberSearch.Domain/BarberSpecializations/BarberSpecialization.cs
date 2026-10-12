using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Specializations;

namespace BarberSearch.Domain.BarberSpecializations;

public class BarberSpecialization : Entity
{
    public Guid BarberProfileId { get; private set; }
    public Guid SpecializationId { get; private set; }

    public BarberProfile BarberProfile { get; private set; } = null!;
    public Specialization Specialization { get; private set; } = null!;

    private BarberSpecialization()
    {
    }

    public BarberSpecialization(
        Guid barberProfileId,
        Guid specializationId)
    {
        BarberProfileId = barberProfileId;
        SpecializationId = specializationId;
    }
}