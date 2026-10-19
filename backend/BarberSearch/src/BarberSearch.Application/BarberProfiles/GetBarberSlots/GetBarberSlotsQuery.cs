using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

namespace BarberSearch.Application.BarberProfiles.GetBarberSlots;

/// <summary>
/// Запрос свободных слотов барбера на дату.
/// </summary>
/// <remarks>
/// <paramref name="SalonId"/> необязателен: без него берётся основной активный салон.
/// <paramref name="ServiceId"/> задаёт длительность слота; без него сетка строится
/// с шагом 30 минут. День считается в часовом поясе салона.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="Date">Локальная дата салона.</param>
/// <param name="ServiceId">Идентификатор услуги каталога; <c>null</c> — шаг 30 минут.</param>
/// <param name="SalonId">Идентификатор салона; <c>null</c> — основной салон.</param>
public record GetBarberSlotsQuery(
    Guid BarberProfileId,
    DateOnly? Date,
    Guid? ServiceId,
    Guid? SalonId) : IQuery<BarberSlotsResponse>;
