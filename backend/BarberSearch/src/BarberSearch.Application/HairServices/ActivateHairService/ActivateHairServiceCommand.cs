using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.HairServices.ActivateHairService;

/// <summary>
/// Команда активации услуги каталога.
/// </summary>
public record ActivateHairServiceCommand(Guid HairServiceId) : ICommand;
