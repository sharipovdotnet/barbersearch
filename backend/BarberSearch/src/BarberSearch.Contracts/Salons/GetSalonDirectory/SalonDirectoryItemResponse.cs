using BarberSearch.Domain.Salons;

namespace BarberSearch.Contracts.Salons.GetSalonDirectory;

/// <summary>
/// Строка публичного каталога салонов для выбора в кабинете барбера.
/// </summary>
/// <remarks>
/// Полей ровно столько, сколько нужно списку выбора: идентификатор, название, город,
/// адрес, часовой пояс и тип места. Контакты, координаты и статус остаются в админском
/// <c>AdminSalonResponse</c>: каталог — не форма редактирования. Каталог отдаёт только
/// перечисленные места (<c>is_listed = true</c>); скрытое место видно лишь с карточки барбера.
/// </remarks>
/// <param name="Id">Идентификатор салона.</param>
/// <param name="Name">Название.</param>
/// <param name="City">Город.</param>
/// <param name="Address">Адрес.</param>
/// <param name="TimeZoneId">Часовой пояс салона (<c>IANA</c>).</param>
/// <param name="Kind">Вид места приёма: барберу видно, куда он примыкает.</param>
public record SalonDirectoryItemResponse(
    Guid Id,
    string Name,
    string City,
    string Address,
    string TimeZoneId,
    PlaceKind Kind);
