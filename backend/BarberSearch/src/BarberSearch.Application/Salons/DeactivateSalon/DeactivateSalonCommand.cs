using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.DeactivateSalon;

/// <summary>
/// Команда деактивации салона.
/// </summary>
public record DeactivateSalonCommand(Guid SalonId) : ICommand;
