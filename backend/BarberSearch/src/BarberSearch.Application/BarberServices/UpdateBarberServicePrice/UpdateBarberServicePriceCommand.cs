using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberServices.UpdateBarberServicePrice;

/// <summary>
/// Команда изменения цены услуги барбера.
/// </summary>
public record UpdateBarberServicePriceCommand(
    Guid BarberProfileId,
    Guid BarberServiceId,
    decimal Price) : ICommand;
