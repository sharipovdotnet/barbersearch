using BarberSearch.Domain.Common;

namespace BarberSearch.Domain.Specializations;

public class Specialization : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    private Specialization()
    {
    }

    public Specialization(
        string name,
        string? description = null)
    {
        Name = name;
        Description = description;
        IsActive = true;
    }
}