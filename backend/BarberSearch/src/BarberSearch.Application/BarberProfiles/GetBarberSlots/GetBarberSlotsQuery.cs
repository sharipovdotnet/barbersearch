using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

namespace BarberSearch.Application.BarberProfiles.GetBarberSlots;

/// <summary>
/// Запрос свободных слотов барбера на дату.
/// </summary>
/// <remarks>
/// <paramref name="SalonId"/> необязателен: без него берётся основной активный салон.
/// <paramref name="HairServiceId"/> — услуга справочника, а не строка «услуга барбера»:
/// она задаёт длительность слота; без неё сетка строится с шагом
/// <c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c> (30 минут). День считается
/// в часовом поясе салона. Для брони нужен <c>barberServiceId</c> из ответа, а не этот
/// идентификатор (R-20).
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="Date">Локальная дата салона.</param>
/// <param name="HairServiceId">
/// Идентификатор услуги справочника (<c>hair_services.id</c>); <c>null</c> — шаг
/// <c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c> (30 минут).
/// </param>
/// <param name="SalonId">Идентификатор салона; <c>null</c> — основной салон.</param>
public record GetBarberSlotsQuery(
    Guid BarberProfileId,
    DateOnly? Date,
    Guid? HairServiceId,
    Guid? SalonId) : IQuery<BarberSlotsResponse>;
