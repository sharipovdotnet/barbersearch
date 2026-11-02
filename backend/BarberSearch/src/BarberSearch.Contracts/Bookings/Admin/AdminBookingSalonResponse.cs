namespace BarberSearch.Contracts.Bookings.Admin;

/// <summary>
/// Салон админской брони: название, адрес, город и часовой пояс.
/// </summary>
/// <remarks>
/// <see cref="TimeZoneId"/> — IANA-идентификатор пояса салона: начало и конец визита
/// приходят в UTC, а показывать их администратору нужно в поясе салона, иначе время
/// в тикете и в интерфейсе не сойдётся. Id нужен для перехода в карточку салона
/// и для точного ответа клиенту, в каком салоне была запись.
/// </remarks>
/// <param name="Id">Идентификатор салона.</param>
/// <param name="Name">Название салона.</param>
/// <param name="Address">Адрес салона.</param>
/// <param name="City">Город салона.</param>
/// <param name="TimeZoneId">IANA-идентификатор часового пояса салона.</param>
public record AdminBookingSalonResponse(
    Guid Id,
    string Name,
    string Address,
    string City,
    string TimeZoneId);
