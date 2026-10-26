using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;

namespace BarberSearch.Application.BarberProfiles.FindBarberProfileSalon;

/// <summary>
/// Запрос поиска салона внутри профиля барбера.
/// </summary>
/// <remarks>
/// Это чтение: обработчик не сохраняет агрегат. Ответ — <see cref="BarberSalonResponse"/>,
/// а не доменный <c>BarberSalon</c>: навигация на профиль наружу не выходит.
/// </remarks>
public record FindBarberProfileSalonQuery(
    Guid BarberProfileId,
    Guid SalonId) : IQuery<BarberSalonResponse>;
