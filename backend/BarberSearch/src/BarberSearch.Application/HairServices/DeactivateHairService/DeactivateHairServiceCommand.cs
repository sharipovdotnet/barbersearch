using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.HairServices.DeactivateHairService;

/// <summary>
/// Команда деактивации услуги каталога.
/// </summary>
public record DeactivateHairServiceCommand(Guid HairServiceId) : ICommand;
