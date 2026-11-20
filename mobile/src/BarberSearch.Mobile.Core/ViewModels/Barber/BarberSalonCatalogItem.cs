using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Domain.Salons;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Строка публичного каталога салонов: то, что можно привязать к профилю.
/// </summary>
/// <remarks>
/// Каталог отдаёт ровно поля выбора — идентификатор, название, город, адрес, пояс и вид
/// места. Контактов и статуса модерации в нём нет: после привязки их покажет
/// <see cref="BarberSalonItem"/> из ответа «Мои салоны», а не догадка из каталога.
/// </remarks>
public sealed class BarberSalonCatalogItem
{
    /// <summary>
    /// Создаёт строку из элемента каталога.
    /// </summary>
    /// <param name="salon">Салон из публичного каталога.</param>
    public BarberSalonCatalogItem(SalonDirectoryItemResponse salon)
    {
        ArgumentNullException.ThrowIfNull(salon);

        Id = salon.Id;
        Name = salon.Name;
        City = salon.City;
        Address = salon.Address;
        TimeZoneId = salon.TimeZoneId;
        Kind = salon.Kind;
    }

    /// <summary>
    /// Идентификатор салона для <c>POST …/salons</c>.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Название салона.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Город салона.
    /// </summary>
    public string City { get; }

    /// <summary>
    /// Адрес салона.
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
    /// Часовой пояс салона (IANA).
    /// </summary>
    public string TimeZoneId { get; }

    /// <summary>
    /// Вид места приёма.
    /// </summary>
    public PlaceKind Kind { get; }

    /// <summary>
    /// Подпись вида места — та же, что у привязанного салона.
    /// </summary>
    public string KindText => BarberSalonItem.PlaceKindText(Kind);
}
