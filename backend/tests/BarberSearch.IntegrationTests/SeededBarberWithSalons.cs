using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Барбер с двумя салонами: первый изначально основной.
/// </summary>
internal sealed record SeededBarberWithSalons(
    User User,
    BarberProfile Profile,
    Salon Primary,
    Salon Secondary);
