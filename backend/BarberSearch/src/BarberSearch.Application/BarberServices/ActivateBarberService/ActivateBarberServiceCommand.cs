using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberServices.ActivateBarberService;

/// <summary>
/// Команда включения услуги барбера.
/// </summary>
public record ActivateBarberServiceCommand(
    Guid BarberProfileId,
    Guid BarberServiceId) : ICommand;
