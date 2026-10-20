using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;

namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

/// <summary>
/// Салон барбера в карточке вместе с расписанием пары (барбер, салон).
/// </summary>
/// <param name="Id">Идентификатор записи «салон барбера».</param>
/// <param name="SalonId">Идентификатор салона справочника.</param>
/// <param name="Name">Название салона.</param>
/// <param name="Address">Адрес салона.</param>
/// <param name="City">Город салона.</param>
/// <param name="Country">Страна салона.</param>
/// <param name="TimeZoneId">IANA-идентификатор часового пояса салона.</param>
/// <param name="IsPrimary">Основной ли это салон барбера.</param>
/// <param name="WorkingSchedules">Недельное расписание барбера в этом салоне.</param>
public record BarberProfileSalonResponse(
    Guid Id,
    Guid SalonId,
    string Name,
    string Address,
    string City,
    string Country,
    string TimeZoneId,
    bool IsPrimary,
    IReadOnlyList<WorkingScheduleResponse> WorkingSchedules);
