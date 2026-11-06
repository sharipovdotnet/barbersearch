using BarberSearch.Domain.Salons;

namespace BarberSearch.Contracts.Salons.Admin;

/// <summary>
/// Строка админского справочника салонов: все поля формы правки и состояние модерации.
/// </summary>
/// <remarks>
/// До этого этапа у салонов не было ни одного чтения, поэтому контракт новый: администратор
/// должен видеть адрес, координаты, контакты и статус, чтобы править и выключать строку.
/// Название приходит как есть, но не редактируется — переименования в домене нет.
/// <para>
/// <c>ownerEmail</c> — связь с владельцем-барбером (<c>null</c> у салона, созданного
/// администратором). Это единственное персональное поле строки и только в админском чтении:
/// барберу в «Мои салоны» владелец не нужен.
/// </para>
/// <para>
/// Статус модерации и <c>isActive</c> — разные вещи: <c>active</c> в фильтре каталога —
/// это <c>isActive &amp;&amp; moderationStatus == Active</c>, <c>pending</c> — очередь проверки.
/// <c>kind</c> и <c>isListed</c> добавлены в #54: скрытое место активно, но в каталог
/// не входит и модерации не требует, поэтому админская строка обязана это показывать.
/// </para>
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
/// <param name="Kind">Вид места приёма.</param>
/// <param name="IsListed">
/// Показывается ли место в публичном каталоге; у скрытого места администратору нужен бейдж
/// «Не в каталоге», иначе непонятно, почему активная строка нигде не видна.
/// </param>
/// <param name="ModerationStatus">Состояние проверки салона администратором.</param>
/// <param name="RejectionReason">Причина отказа; <c>null</c>, если салон не отклонён.</param>
/// <param name="ReviewedAt">Когда принято решение; <c>null</c>, если салон ещё не проверяли.</param>
/// <param name="OwnerEmail">Email владельца-барбера; <c>null</c> у админского салона.</param>
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
    PlaceKind Kind,
    bool IsListed,
    SalonModerationStatus ModerationStatus,
    string? RejectionReason,
    DateTimeOffset? ReviewedAt,
    string? OwnerEmail,
    int UsageCount);
