using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberServices.DeactivateBarberService;

/// <summary>
/// Команда выключения услуги барбера.
/// </summary>
public record DeactivateBarberServiceCommand(
    Guid BarberProfileId,
    Guid BarberServiceId) : ICommand;
