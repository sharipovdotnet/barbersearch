namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;

/// <summary>
/// Привязанный к профилю салон в списке «Мои салоны».
/// </summary>
/// <remarks>
/// Только чтение: расписание пары (барбер, салон) отдаёт
/// <c>GET api/barber-profiles/{id}/schedule</c>, а здесь нужны название, адрес,
/// часовой пояс и признак основного.
/// </remarks>
/// <param name="SalonId">Идентификатор салона.</param>
/// <param name="Name">Название.</param>
/// <param name="City">Город.</param>
/// <param name="Address">Адрес.</param>
/// <param name="TimeZoneId">Часовой пояс салона (<c>IANA</c>).</param>
/// <param name="IsPrimary">Основной салон профиля.</param>
public record BarberSalonSummaryResponse(
    Guid SalonId,
    string Name,
    string City,
    string Address,
    string TimeZoneId,
    bool IsPrimary);
