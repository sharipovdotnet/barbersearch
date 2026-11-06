namespace BarberSearch.Contracts.Salons.GetSalonDirectory;

/// <summary>
/// Строка публичного каталога салонов для выбора в кабинете барбера.
/// </summary>
/// <remarks>
/// Полей ровно столько, сколько нужно списку выбора: идентификатор, название, город,
/// адрес и часовой пояс. Контакты, координаты и статус остаются в админском
/// <c>AdminSalonResponse</c>: каталог — не форма редактирования.
/// </remarks>
/// <param name="Id">Идентификатор салона.</param>
/// <param name="Name">Название.</param>
/// <param name="City">Город.</param>
/// <param name="Address">Адрес.</param>
/// <param name="TimeZoneId">Часовой пояс салона (<c>IANA</c>).</param>
public record SalonDirectoryItemResponse(
    Guid Id,
    string Name,
    string City,
    string Address,
    string TimeZoneId);
