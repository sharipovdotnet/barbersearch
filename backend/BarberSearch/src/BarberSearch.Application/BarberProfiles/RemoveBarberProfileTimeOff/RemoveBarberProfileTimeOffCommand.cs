using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileTimeOff;

/// <summary>
/// Команда удаления нерабочего периода (отпуска) барбера.
/// </summary>
public record RemoveBarberProfileTimeOffCommand(
    Guid BarberProfileId,
    Guid TimeOffId) : ICommand;
