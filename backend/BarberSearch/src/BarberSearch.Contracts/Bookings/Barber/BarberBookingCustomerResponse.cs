namespace BarberSearch.Contracts.Bookings.Barber;

/// <summary>
/// Клиент в записи барбера.
/// </summary>
/// <remarks>
/// Только то, что нужно для звонка: имя и телефон. Email клиента — не дело барбера,
/// поэтому в контракт не входит; телефон приходит как есть и не используется в URL сервиса.
/// </remarks>
/// <param name="DisplayName">Имя и фамилия клиента, нормализованные пробелы.</param>
/// <param name="PhoneNumber">Телефон клиента; <c>null</c>, если он не заполнен.</param>
public record BarberBookingCustomerResponse(
    string DisplayName,
    string? PhoneNumber);
