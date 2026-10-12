using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;

namespace BarberSearch.Domain.FavoriteBarbers;

public class FavoriteBarber : Entity
{
    public Guid UserId { get; private set; }
    public Guid BarberProfileId { get; private set; }

    public User User { get; private set; } = null!;
    public BarberProfile BarberProfile { get; private set; } = null!;

    private FavoriteBarber()
    {
    }

    public FavoriteBarber(
        Guid userId,
        Guid barberProfileId)
    {
        UserId = userId;
        BarberProfileId = barberProfileId;
    }
}