using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Услуга в карточке: строка прайса и пункт выбора.
/// </summary>
/// <remarks>
/// Один и тот же объект живёт в списке «Услуги» и в <c>Picker</c> «Свободное время»: цена
/// и длительность приходят из одной строки прайса, и расходить им нельзя. Нулевой
/// <see cref="HairServiceId"/> — синтетический пункт «Любая услуга»: сервер в этом режиме
/// отдаёт шаг сетки 30 минут и <c>barberServiceId = null</c>, поэтому записаться нельзя.
/// </remarks>
public sealed class BarberServiceOption
{
    private BarberServiceOption(
        Guid? hairServiceId,
        string name,
        decimal price,
        string priceText,
        string durationText,
        string displayText)
    {
        HairServiceId = hairServiceId;
        Name = name;
        Price = price;
        PriceText = priceText;
        DurationText = durationText;
        DisplayText = displayText;
    }

    /// <summary>
    /// Услуга справочника, которую принимают поиск и слоты; <c>null</c> — «Любая услуга».
    /// </summary>
    public Guid? HairServiceId { get; }

    /// <summary>
    /// Название услуги.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Цена у этого барбера; для «Любой услуги» — ноль, он никогда не попадает в бронь.
    /// </summary>
    public decimal Price { get; }

    /// <summary>
    /// Цена для показа: «2 400 ₽».
    /// </summary>
    public string PriceText { get; }

    /// <summary>
    /// Длительность для показа: «45 мин»; пустая для «Любой услуги».
    /// </summary>
    public string DurationText { get; }

    /// <summary>
    /// Подпись в <c>Picker</c>: настоящее имя услуги или «Любая услуга».
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Пункт «Любая услуга»: слоты видны, но запись недоступна.
    /// </summary>
    /// <returns>Пункт без услуги справочника.</returns>
    public static BarberServiceOption AnyService() =>
        new(null, "Любая услуга", 0m, string.Empty, string.Empty, "Любая услуга");

    /// <summary>
    /// Собирает пункт из строки прайса карточки.
    /// </summary>
    /// <param name="service">Услуга барбера с ценой и длительностью.</param>
    /// <returns>Пункт для списка и <c>Picker</c>.</returns>
    public static BarberServiceOption From(BarberProfileServiceResponse service)
    {
        ArgumentNullException.ThrowIfNull(service);

        return new BarberServiceOption(
            service.HairServiceId,
            service.Name,
            service.Price,
            BarberText.Price(service.Price),
            BarberText.Duration(service.DurationMinutes),
            service.Name);
    }
}
