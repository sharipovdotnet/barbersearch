namespace BarberSearch.Contracts.Salons.Admin;

/// <summary>
/// Строка админского справочника салонов: все поля формы правки.
/// </summary>
/// <remarks>
/// До этого этапа у салонов не было ни одного чтения, поэтому контракт новый: администратор
/// должен видеть адрес, координаты, контакты и статус, чтобы править и выключать строку.
/// Название приходит как есть, но не редактируется — переименования в домене нет.
/// Ответ — полный справочник без пагинации.
/// </remarks>
/// <param name="Id">Идентификатор салона.</param>
/// <param name="Name">Название; задаётся при создании и не меняется.</param>
/// <param name="Address">Адрес.</param>
/// <param name="City">Город.</param>
/// <param name="PostalCode">Почтовый индекс; <c>null</c>, если его нет.</param>
/// <param name="Country">Страна.</param>
/// <param name="TimeZoneId">Часовой пояс IANA, например <c>Europe/Moscow</c>.</param>
/// <param name="Latitude">Широта.</param>
/// <param name="Longitude">Долгота.</param>
/// <param name="PhoneNumber">Телефон; <c>null</c>, если его нет.</param>
/// <param name="WebsiteUrl">Сайт; <c>null</c>, если его нет.</param>
/// <param name="IsActive">Виден ли салон в поиске и на карточках.</param>
/// <param name="UsageCount">
/// Сколько активных и подтверждённых профилей барберов работают в салоне: считается
/// <c>DISTINCT barber_profile_id</c> по <c>barber_salons</c> с условием «профиль активен
/// и подтверждён». Выключение салона не удаляет эти связи.
/// </param>
public record AdminSalonResponse(
    Guid Id,
    string Name,
    string Address,
    string City,
    string? PostalCode,
    string Country,
    string TimeZoneId,
    double Latitude,
    double Longitude,
    string? PhoneNumber,
    string? WebsiteUrl,
    bool IsActive,
    int UsageCount);
