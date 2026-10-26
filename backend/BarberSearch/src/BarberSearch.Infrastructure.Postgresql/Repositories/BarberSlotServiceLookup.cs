namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Пара «услуга барбера — услуга справочника», по которой считается сетка слотов.
/// </summary>
/// <remarks>
/// Идентификатор и длительность обязаны прийти из одной строки <c>barber_services</c>:
/// иначе клиент получит <c>barberServiceId</c> от одной пары, а длительность — от другой (R-20).
/// Уникальный индекс <c>ux_barber_services_barber_profile_id_hair_service_id</c> гарантирует,
/// что у профиля не больше одной строки на услугу справочника.
/// </remarks>
/// <param name="BarberServiceId">Идентификатор строки <c>barber_services</c>.</param>
/// <param name="HairServiceId">Идентификатор услуги справочника.</param>
/// <param name="DurationMinutes">Длительность этой пары.</param>
internal sealed record BarberSlotServiceLookup(
    Guid BarberServiceId,
    Guid HairServiceId,
    int DurationMinutes);
