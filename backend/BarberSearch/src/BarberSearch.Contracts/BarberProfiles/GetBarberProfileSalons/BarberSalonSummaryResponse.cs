using BarberSearch.Domain.Salons;

namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;

/// <summary>
/// Привязанный к профилю салон в списке «Мои салоны».
/// </summary>
/// <remarks>
/// Только чтение: расписание пары (барбер, салон) отдаёт
/// <c>GET api/barber-profiles/{id}/schedule</c>, а здесь нужны название, адрес,
/// часовой пояс, признак основного и состояние модерации.
/// <para>
/// Салон на проверке и отклонённый салон <b>показываются</b>: это список работы барбера,
/// а не витрина, и владелец должен видеть, что салон ещё не в поиске и почему.
/// </para>
/// <para>
/// Полные поля формы (страна, индекс, координаты, контакты) нужны владельцу для правки:
/// адрес и контакты — два независимых <c>PUT</c>, и форма должна быть предзаполнена текущими
/// значениями, иначе сохранение затрёт координаты, которые владелец не вводил. <c>ownerUserId</c>
/// позволяет кабинету показать правку только на своих салонах: салон, созданный администратором,
/// привязан к профилю, но его владельца нет (<c>null</c>).
/// </para>
/// </remarks>
/// <param name="SalonId">Идентификатор салона.</param>
/// <param name="Name">Название.</param>
/// <param name="City">Город.</param>
/// <param name="Address">Адрес.</param>
/// <param name="TimeZoneId">Часовой пояс салона (<c>IANA</c>).</param>
/// <param name="IsPrimary">Основной салон профиля.</param>
/// <param name="Country">Страна.</param>
/// <param name="PostalCode">Почтовый индекс; <c>null</c>, если его нет.</param>
/// <param name="Latitude">Широта.</param>
/// <param name="Longitude">Долгота.</param>
/// <param name="PhoneNumber">Телефон; <c>null</c>, если его нет.</param>
/// <param name="WebsiteUrl">Сайт; <c>null</c>, если его нет.</param>
/// <param name="OwnerUserId">Владелец салона; <c>null</c> у салона, созданного администратором.</param>
/// <param name="ModerationStatus">Состояние проверки салона администратором.</param>
/// <param name="RejectionReason">Причина отказа; <c>null</c>, если салон не отклонён.</param>
public record BarberSalonSummaryResponse(
    Guid SalonId,
    string Name,
    string City,
    string Address,
    string TimeZoneId,
    bool IsPrimary,
    string Country,
    string? PostalCode,
    double Latitude,
    double Longitude,
    string? PhoneNumber,
    string? WebsiteUrl,
    Guid? OwnerUserId,
    SalonModerationStatus ModerationStatus,
    string? RejectionReason);
