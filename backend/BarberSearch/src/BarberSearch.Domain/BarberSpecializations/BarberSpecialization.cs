using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;

namespace BarberSearch.Domain.BarberSpecializations;

/// <summary>
/// Специализация конкретного барбера. Дочерняя сущность агрегата <see cref="BarberProfile"/>:
/// создаётся только методами агрегата.
/// </summary>
public class BarberSpecialization : Entity
{
    public Guid BarberProfileId { get; private set; }
    public Guid SpecializationId { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    // Ссылки на Specialization здесь нет — это граница другого агрегата.
    public BarberProfile BarberProfile { get; private set; } = null!;

    private BarberSpecialization()
    {
    }

    private BarberSpecialization(Guid barberProfileId, Guid specializationId)
    {
        BarberProfileId = barberProfileId;
        SpecializationId = specializationId;
    }

    /// <summary>
    /// Создаёт связь от имени агрегата <c>BarberProfile</c>.
    /// </summary>
    internal static BarberSpecialization Create(Guid barberProfileId, Guid specializationId)
        => new(barberProfileId, specializationId);
}
