using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.ActivateSalon;

/// <summary>
/// Команда активации салона.
/// </summary>
public record ActivateSalonCommand(Guid SalonId) : ICommand;
