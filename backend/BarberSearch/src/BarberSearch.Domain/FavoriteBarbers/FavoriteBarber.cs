using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;

namespace BarberSearch.Domain.FavoriteBarbers;

/// <summary>
/// Избранный барбер пользователя. Дочерняя сущность агрегата <c>User</c>:
/// создаётся только методами агрегата.
/// </summary>
public class FavoriteBarber : Entity
{
    public Guid UserId { get; private set; }
    public Guid BarberProfileId { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    // Ссылки на BarberProfile здесь нет — это граница другого агрегата.
    public Users.User User { get; private set; } = null!;

    private FavoriteBarber()
    {
    }

    private FavoriteBarber(Guid userId, Guid barberProfileId)
    {
        UserId = userId;
        BarberProfileId = barberProfileId;
    }

    /// <summary>
    /// Создаёт запись от имени агрегата <c>User</c>.
    /// </summary>
    internal static FavoriteBarber Create(Guid userId, Guid barberProfileId)
        => new(userId, barberProfileId);
}
