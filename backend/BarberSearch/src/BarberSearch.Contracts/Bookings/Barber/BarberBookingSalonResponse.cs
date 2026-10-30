namespace BarberSearch.Contracts.Bookings.Barber;

/// <summary>
/// Салон визита в записи барбера.
/// </summary>
/// <remarks>
/// Адрес и город — отдельными полями, как в таблице <c>salons</c>: клиент показывает их
/// по-разному на узком и широком экране. Часовой пояс салона нужен, чтобы время визита
/// было показано в поясе салона, а не браузера.
/// </remarks>
/// <param name="Name">Название салона.</param>
/// <param name="Address">Адрес салона.</param>
/// <param name="City">Город салона.</param>
/// <param name="TimeZoneId">Идентификатор часового пояса IANA.</param>
public record BarberBookingSalonResponse(
    string Name,
    string Address,
    string City,
    string TimeZoneId);
