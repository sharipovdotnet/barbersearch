namespace BarberSearch.Contracts.BarberProfiles.BarberSchedule;

/// <summary>
/// Расписание барбера в одном салоне.
/// </summary>
/// <remarks>
/// <see cref="Weekly"/> всегда содержит семь дней, даже если в базе строк меньше:
/// отсутствующий день — это выходной. <see cref="Exceptions"/> ограничены запрошенным
/// окном дат; причина исключения может быть <c>null</c>.
/// </remarks>
/// <param name="SalonId">Салон, к которому относится расписание.</param>
/// <param name="Name">Название салона.</param>
/// <param name="City">Город салона.</param>
/// <param name="TimeZoneId">Часовой пояс салона: в нём задано время.</param>
/// <param name="IsPrimary">Основной салон барбера.</param>
/// <param name="Weekly">Недельный шаблон, семь дней.</param>
/// <param name="Exceptions">Исключения на конкретные даты в запрошенном окне.</param>
public record BarberScheduleSalonResponse(
    Guid SalonId,
    string Name,
    string City,
    string TimeZoneId,
    bool IsPrimary,
    IReadOnlyList<BarberScheduleDayResponse> Weekly,
    IReadOnlyList<BarberScheduleExceptionResponse> Exceptions);
