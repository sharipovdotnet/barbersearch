using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberSalons.RemoveScheduleException;

/// <summary>
/// Команда снятия исключения с конкретной даты.
/// </summary>
/// <param name="BarberProfileId">Профиль барбера — корень агрегата.</param>
/// <param name="SalonId">Салон, к которому относится дата.</param>
/// <param name="Date">Локальная дата салона.</param>
public record RemoveScheduleExceptionCommand(
    Guid BarberProfileId,
    Guid SalonId,
    DateOnly Date) : ICommand;
