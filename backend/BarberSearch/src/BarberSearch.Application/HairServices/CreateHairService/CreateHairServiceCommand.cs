using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.HairServices;

namespace BarberSearch.Application.HairServices.CreateHairService;

/// <summary>
/// Команда создания услуги каталога.
/// </summary>
public record CreateHairServiceCommand(
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    string? Description) : ICommand;
