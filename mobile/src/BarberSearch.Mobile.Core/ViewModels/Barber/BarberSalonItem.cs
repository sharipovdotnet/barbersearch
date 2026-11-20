using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Строка списка «Мои салоны»: место, статус модерации и мобильные подсказки.
/// </summary>
/// <remarks>
/// <para>
/// Салон — не витрина, а рабочий список: на проверке и отклонённый тоже видны, иначе мастер
/// не понял бы, почему его нет в поиске. Поэтому строка показывает и статус, и причину
/// отказа, а «Не в каталоге» стоит отдельным признаком: одобренный салон может быть скрыт
/// из каталога, и это другое состояние, а не другой статус.
/// </para>
/// <para>
/// Для телефона строка несёт данные, из которых страница делает действия: телефон — для
/// <c>tel:</c>, координаты и адрес — для карты и копирования. Сам Core MAUI не знает и
/// никаких ссылок не собирает: он отдаёт значения, а решение «звонить или открыть карту»
/// остаётся у страницы.
/// </para>
/// </remarks>
public sealed class BarberSalonItem
{
    /// <summary>
    /// Создаёт строку из привязанного салона.
    /// </summary>
    /// <param name="salon">Салон из ответа «Мои салоны».</param>
    public BarberSalonItem(BarberSalonSummaryResponse salon)
    {
        ArgumentNullException.ThrowIfNull(salon);

        SalonId = salon.SalonId;
        Name = salon.Name;
        City = salon.City;
        Address = salon.Address;
        Country = salon.Country;
        PostalCode = salon.PostalCode;
        TimeZoneId = salon.TimeZoneId;
        Latitude = salon.Latitude;
        Longitude = salon.Longitude;
        PhoneNumber = salon.PhoneNumber;
        WebsiteUrl = salon.WebsiteUrl;
        IsPrimary = salon.IsPrimary;
        ModerationStatus = salon.ModerationStatus;
        IsListed = salon.IsListed;
        Kind = salon.Kind;
        RejectionReason = salon.RejectionReason;
    }

    /// <summary>
    /// Идентификатор салона.
    /// </summary>
    public Guid SalonId { get; }

    /// <summary>
    /// Название салона.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Город салона.
    /// </summary>
    public string City { get; }

    /// <summary>
    /// Адрес салона из ответа.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Город и адрес одной строкой: «Екатеринбург, Ленина, 10».
    /// </summary>
    public string FullAddress
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Address))
            {
                return City;
            }

            return string.IsNullOrWhiteSpace(City)
                ? Address
                : string.Concat(City, ", ", Address);
        }
    }

    /// <summary>
    /// Адрес заполнен: страница может показать «На карте» и скопировать строку.
    /// </summary>
    public bool HasAddress => !string.IsNullOrWhiteSpace(FullAddress);

    /// <summary>
    /// Страна салона.
    /// </summary>
    public string Country { get; }

    /// <summary>
    /// Почтовый индекс; <c>null</c>, если его нет.
    /// </summary>
    public string? PostalCode { get; }

    /// <summary>
    /// Часовой пояс салона (IANA): по нему строится расписание пары.
    /// </summary>
    public string TimeZoneId { get; }

    /// <summary>
    /// Пояс салона строкой для карточки: «Часовой пояс: Asia/Yekaterinburg».
    /// </summary>
    public string TimeZoneText => Loc.T("Часовой пояс: {0}", TimeZoneId);

    /// <summary>
    /// Широта салона: из неё страница собирает точку на карте.
    /// </summary>
    public double Latitude { get; }

    /// <summary>
    /// Долгота салона: из неё страница собирает точку на карте.
    /// </summary>
    public double Longitude { get; }

    /// <summary>
    /// Координаты заполнены: <c>0, 0</c> считается отсутствием точки.
    /// </summary>
    public bool HasCoordinates => Latitude != 0 || Longitude != 0;

    /// <summary>
    /// Телефон салона; <c>null</c>, если его нет.
    /// </summary>
    public string? PhoneNumber { get; }

    /// <summary>
    /// Телефон есть: страница может показать «Позвонить» и собрать <c>tel:</c>.
    /// </summary>
    public bool HasPhoneNumber => !string.IsNullOrWhiteSpace(PhoneNumber);

    /// <summary>
    /// Сайт салона; <c>null</c>, если его нет.
    /// </summary>
    public string? WebsiteUrl { get; }

    /// <summary>
    /// Сайт есть: страница может открыть его в браузере.
    /// </summary>
    public bool HasWebsiteUrl => !string.IsNullOrWhiteSpace(WebsiteUrl);

    /// <summary>
    /// Салон основной: записи и расписание по умолчанию открываются на нём.
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Подпись «Основной»; пустая строка, если салон не основной.
    /// </summary>
    public string PrimaryBadgeText => IsPrimary ? Loc.T("Основной") : string.Empty;

    /// <summary>
    /// Состояние проверки салона администратором.
    /// </summary>
    public SalonModerationStatus ModerationStatus { get; }

    /// <summary>
    /// Салон показан в каталоге: видимость в поиске — отдельный признак модерации.
    /// </summary>
    public bool IsListed { get; }

    /// <summary>
    /// Салон скрыт из каталога: «Не в каталоге» вместо статуса проверки.
    /// </summary>
    public bool IsNotListed => !IsListed;

    /// <summary>
    /// Состояние салона словом: «Одобрен», «На проверке», «Отклонён» или «Не в каталоге».
    /// </summary>
    public string StatusText => IsListed
        ? BarberSalonText.ModerationText(ModerationStatus)
        : BarberSalonText.NotListed;

    /// <summary>
    /// Короткий ключ состояния для класса строки в разметке.
    /// </summary>
    public string StatusKind => !IsListed
        ? "notlisted"
        : ModerationStatus switch
        {
            SalonModerationStatus.Active => "approved",
            SalonModerationStatus.Rejected => "rejected",
            _ => "pending",
        };

    /// <summary>
    /// Салон одобрен, но ещё не в поиске: подсказка объясняет, что делать.
    /// </summary>
    public string PendingHint => IsListed && ModerationStatus == SalonModerationStatus.Pending
        ? BarberSalonText.PendingHint
        : string.Empty;

    /// <summary>
    /// Салон отклонён: карточка показывает причину.
    /// </summary>
    public bool IsRejected => ModerationStatus == SalonModerationStatus.Rejected;

    /// <summary>
    /// Причина отказа администратора; <c>null</c>, если салон не отклонён.
    /// </summary>
    public string? RejectionReason { get; }

    /// <summary>
    /// Подписанная причина отказа либо «Причина отказа: не указана».
    /// </summary>
    public string RejectionReasonText
    {
        get
        {
            if (!IsRejected)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(RejectionReason)
                ? Loc.T("Причина отказа: не указана")
                : BarberSalonText.Rejection(RejectionReason);
        }
    }

    /// <summary>
    /// Вид места приёма из ответа.
    /// </summary>
    public PlaceKind Kind { get; }

    /// <summary>
    /// Подпись вида места: «Барбершоп», «Студия», «Кабинет», «На дому», «Выезд».
    /// </summary>
    public string KindText => PlaceKindText(Kind);

    /// <summary>
    /// Русская подпись вида места — общая для списка и каталога.
    /// </summary>
    /// <param name="kind">Вид места из ответа.</param>
    /// <returns>Подпись для карточки.</returns>
    public static string PlaceKindText(PlaceKind kind) => kind switch
    {
        PlaceKind.RentalStudio => Loc.T("Студия"),
        PlaceKind.SoloStudio => Loc.T("Кабинет"),
        PlaceKind.Home => Loc.T("На дому"),
        PlaceKind.Mobile => Loc.T("Выезд"),
        _ => Loc.T("Барбершоп"),
    };
}
