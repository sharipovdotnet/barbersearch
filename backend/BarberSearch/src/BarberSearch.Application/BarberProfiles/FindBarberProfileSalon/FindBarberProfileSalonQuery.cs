using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.BarberSalons;

namespace BarberSearch.Application.BarberProfiles.FindBarberProfileSalon;

/// <summary>
/// Запрос поиска салона внутри профиля барбера.
/// </summary>
/// <remarks>
/// Это чтение: обработчик не сохраняет агрегат. Найденный салон — точка входа
/// для работы с расписанием пары (барбер, салон).
/// </remarks>
public record FindBarberProfileSalonQuery(
    Guid BarberProfileId,
    Guid SalonId) : IQuery<BarberSalon>;
