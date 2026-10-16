namespace BarberSearch.Contracts.BarberProfiles.AddBarberProfileSalon;

/// <summary>
/// Данные запроса на добавление салона в профиль барбера.
/// </summary>
/// <remarks>
/// <see cref="IsPrimary"/> = <c>true</c> делает новый салон основным и снимает признак
/// с прежнего основного — это правило домена, а не транспорта.
/// </remarks>
public record AddBarberProfileSalonRequest(
    Guid SalonId,
    bool IsPrimary = false);
