using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileSalon;

/// <summary>
/// Команда добавления салона в профиль барбера.
/// </summary>
/// <remarks>
/// <see cref="IsPrimary"/> = <c>true</c> делает новый салон основным и снимает признак
/// с прежнего основного — это правило домена.
/// </remarks>
public record AddBarberProfileSalonCommand(
    Guid BarberProfileId,
    Guid SalonId,
    bool IsPrimary) : ICommand;
