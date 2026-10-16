using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.HairServices.UpdateHairService;

/// <summary>
/// Команда изменения услуги каталога.
/// </summary>
/// <remarks>
/// Категория не меняется: цена и длительность конкретных барберов привязаны к услуге,
/// а перевод услуги в другую категорию менял бы смысл уже сделанных предложений.
/// </remarks>
public record UpdateHairServiceCommand(
    Guid HairServiceId,
    string Name,
    string? Description,
    int DefaultDurationMinutes) : ICommand;
