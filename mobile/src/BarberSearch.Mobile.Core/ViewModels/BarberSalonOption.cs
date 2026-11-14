using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Салон в карточке: место приёма, его пояс и пункт выбора.
/// </summary>
/// <remarks>
/// Пояс салона — не украшение: от него зависит и «сегодня» на календаре, и время в слотах.
/// Поэтому он хранится рядом с идентификатором, а не вытаскивается из карточки каждый раз.
/// </remarks>
public sealed class BarberSalonOption
{
    private BarberSalonOption(
        Guid salonId,
        string name,
        string city,
        string timeZoneId,
        PlaceKind kind,
        bool isPrimary,
        string displayText)
    {
        SalonId = salonId;
        Name = name;
        City = city;
        TimeZoneId = timeZoneId;
        Kind = kind;
        IsPrimary = isPrimary;
        DisplayText = displayText;
    }

    /// <summary>
    /// Идентификатор салона справочника: его принимают слоты и бронь.
    /// </summary>
    public Guid SalonId { get; }

    /// <summary>
    /// Название салона.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Город салона: шапка показывает место приёма вместе с городом.
    /// </summary>
    public string City { get; }

    /// <summary>
    /// IANA-идентификатор часового пояса салона.
    /// </summary>
    public string TimeZoneId { get; }

    /// <summary>
    /// Вид места приёма: барбершоп, кабинет, приём на дому или выезд.
    /// </summary>
    public PlaceKind Kind { get; }

    /// <summary>
    /// Основной ли это салон барбера: с него открывается карточка.
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Подпись в <c>Picker</c>: «Название · Барбершоп».
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Собирает пункт из салона карточки.
    /// </summary>
    /// <param name="salon">Салон барбера вместе с расписанием.</param>
    /// <returns>Пункт для <c>Picker</c> и выбора места.</returns>
    public static BarberSalonOption From(BarberProfileSalonResponse salon)
    {
        ArgumentNullException.ThrowIfNull(salon);

        return new BarberSalonOption(
            salon.SalonId,
            salon.Name,
            salon.City,
            salon.TimeZoneId,
            salon.Kind,
            salon.IsPrimary,
            string.Concat(salon.Name, " · ", BarberText.PlaceKindText(salon.Kind)));
    }
}
