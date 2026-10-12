using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Common;

namespace BarberSearch.Domain.HairServices;

public class HairService : AggregateRoot
{
    private readonly List<BarberService> _barbers = new();

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public ServiceCategory Category { get; private set; }

    public int DefaultDurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<BarberService> Barbers
        => _barbers.AsReadOnly();

    private HairService()
    {
    }

    public HairService(
        string name,
        ServiceCategory category,
        int defaultDurationMinutes,
        string? description = null)
    {
        Name = name;
        Category = category;
        DefaultDurationMinutes = defaultDurationMinutes;
        Description = description;

        IsActive = true;
    }

    public void Update(
        string name,
        string? description,
        int defaultDurationMinutes)
    {
        Name = name;
        Description = description;
        DefaultDurationMinutes = defaultDurationMinutes;

        MarkUpdated();
    }
}