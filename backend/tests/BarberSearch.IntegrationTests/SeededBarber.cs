using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Забронированный набор справочников: барбер, его салон и услуга.
/// </summary>
internal sealed record SeededBarber(
    User User,
    BarberProfile Profile,
    Salon Salon,
    HairService HairService,
    BarberService Service);
