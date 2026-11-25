using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Строка прайса мастера: услуга справочника с ценой и длительностью.
/// </summary>
/// <remarks>
/// Две идентификатора — разные сущности: <see cref="BarberServiceId"/> адресует строку
/// прайса, <see cref="HairServiceId"/> — услугу справочника, по которой ищут клиенты.
/// Правки и скрытие идут парой «профиль + <see cref="HairServiceId"/>», поэтому оба
/// значения хранятся рядом, а не выводятся друг из друга.
/// </remarks>
public sealed class BarberServiceItem
{
    /// <summary>
    /// Создаёт строку из ответа сервера.
    /// </summary>
    /// <param name="service">Услуга из собственной карточки.</param>
    public BarberServiceItem(BarberProfileServiceResponse service)
    {
        ArgumentNullException.ThrowIfNull(service);

        BarberServiceId = service.BarberServiceId;
        HairServiceId = service.HairServiceId;
        Name = service.Name;
        Category = service.Category;
        CategoryText = BarberProfileText.CategoryText(service.Category);
        SpecializationName = service.SpecializationName;
        DefaultDurationMinutes = service.DefaultDurationMinutes;
        Price = service.Price;
        DurationMinutes = service.DurationMinutes;
        IsActive = service.IsActive;
    }

    /// <summary>
    /// Идентификатор строки прайса.
    /// </summary>
    public Guid BarberServiceId { get; }

    /// <summary>
    /// Идентификатор услуги справочника: его принимают правка и скрытие.
    /// </summary>
    public Guid HairServiceId { get; }

    /// <summary>
    /// Название услуги.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Категория справочника.
    /// </summary>
    public ServiceCategory Category { get; }

    /// <summary>
    /// Категория для показа: «Стрижка», «Борода».
    /// </summary>
    public string CategoryText { get; }

    /// <summary>
    /// Специализация для показа: «Фейд», «Оформление бороды».
    /// </summary>
    public string SpecializationName { get; }

    /// <summary>
    /// Длительность по умолчанию из справочника: её подставляет редактор.
    /// </summary>
    public int DefaultDurationMinutes { get; }

    /// <summary>
    /// Цена у этого мастера.
    /// </summary>
    public decimal Price { get; }

    /// <summary>
    /// Цена для показа: «2 400 ₽».
    /// </summary>
    public string PriceText => BarberProfileText.PriceText(Price);

    /// <summary>
    /// Длительность у этого мастера в минутах.
    /// </summary>
    public int DurationMinutes { get; }

    /// <summary>
    /// Длительность для показа: «45 мин».
    /// </summary>
    public string DurationText => BarberProfileText.DurationText(DurationMinutes);

    /// <summary>
    /// Можно ли записаться на услугу.
    /// </summary>
    public bool IsActive { get; }

    /// <summary>
    /// Услуга скрыта из прайса и поиска.
    /// </summary>
    public bool IsHidden => !IsActive;

    /// <summary>
    /// Бейдж строки: пусто у активной, «Скрыта» у скрытой.
    /// </summary>
    public string StatusText => BarberProfileText.ServiceStatusText(IsActive);
}
