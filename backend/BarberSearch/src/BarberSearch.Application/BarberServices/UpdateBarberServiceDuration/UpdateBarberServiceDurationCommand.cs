using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberServices.UpdateBarberServiceDuration;

/// <summary>
/// Команда изменения длительности услуги барбера.
/// </summary>
public record UpdateBarberServiceDurationCommand(
    Guid BarberProfileId,
    Guid BarberServiceId,
    int DurationMinutes) : ICommand;
