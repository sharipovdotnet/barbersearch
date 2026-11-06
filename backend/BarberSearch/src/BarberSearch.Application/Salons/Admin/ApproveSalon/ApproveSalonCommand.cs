using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.Admin.ApproveSalon;

/// <summary>
/// Команда одобрения салона на модерации.
/// </summary>
/// <param name="SalonId">Идентификатор салона.</param>
public record ApproveSalonCommand(Guid SalonId) : ICommand;
