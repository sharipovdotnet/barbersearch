using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.SetBarberProfilePrimarySalon;

/// <summary>
/// Команда назначения основного салона барбера.
/// </summary>
/// <remarks>
/// У профиля одновременно может быть только один основной салон: назначение нового
/// снимает признак с прежнего — это правило домена.
/// </remarks>
public record SetBarberProfilePrimarySalonCommand(
    Guid BarberProfileId,
    Guid SalonId) : ICommand;
