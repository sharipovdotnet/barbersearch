using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Услуга в карточке: строка прайса и пункт выбора.
/// </summary>
/// <remarks>
/// Один и тот же объект живёт в списке «Услуги» и в фильтре «Свободное время»: цена и
/// длительность приходят из одной строки прайса, и расходить им нельзя. Нулевой
/// <see cref="HairServiceId"/> — синтетический пункт «Любая услуга»: сервер в этом режиме
/// отдаёт шаг сетки 30 минут и <c>barberServiceId = null</c>, поэтому записаться нельзя.
/// </remarks>
public sealed class BarberServiceOption
{
    private BarberServiceOption(
        Guid? hairServiceId,
        string name,
        ServiceCategory category,
        string categoryText,
        Guid specializationId,
        string specializationName,
        decimal price,
        string priceText,
        string durationText,
        string displayText)
    {
        HairServiceId = hairServiceId;
        Name = name;
        Category = category;
        CategoryText = categoryText;
        SpecializationId = specializationId;
        SpecializationName = specializationName;
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
    /// Название для строки прайса: маркер «• » и название услуги.
    /// </summary>
    /// <remarks>
    /// Маркер свёрстан текстом, как у пунктов «О мастере»: под заголовком специализации услуги
    /// читаются перечислением. <see cref="DisplayText"/> для поля выбора остаётся без маркера,
    /// иначе «Любая услуга» и пункты выбора начинались бы с точки.
    /// </remarks>
    public string DisplayName => string.Concat("• ", Name);

    /// <summary>
    /// Категория услуги справочника; у «Любой услуги» — <see cref="ServiceCategory.Other"/>.
    /// </summary>
    public ServiceCategory Category { get; }

    /// <summary>
    /// Категория для бейджа: «Стрижка», «Борода»; у «Любой услуги» пусто.
    /// </summary>
    public string CategoryText { get; }

    /// <summary>
    /// Идентификатор специализации услуги; у «Любой услуги» — <see cref="Guid.Empty"/>.
    /// </summary>
    /// <remarks>
    /// Ключ группировки фильтра «Услуга»: одноимённые, но разные специализации остаются
    /// разными группами, как в вебе (<c>groupBarberServices</c>).
    /// </remarks>
    public Guid SpecializationId { get; }

    /// <summary>
    /// Специализация услуги: заголовок её группы в прайсе. У «Любой услуги» пусто.
    /// </summary>
    public string SpecializationName { get; }

    /// <summary>
    /// Цена у этого мастера; для «Любой услуги» — ноль, он никогда не попадает в бронь.
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
    /// Подпись в поле выбора и в строке экрана «Услуга»: настоящее имя услуги или «Любая услуга».
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Пункт «Любая услуга»: слоты видны, но запись недоступна.
    /// </summary>
    /// <returns>Пункт без услуги справочника.</returns>
    public static BarberServiceOption AnyService()
    {
        var text = Loc.T("Любая услуга");

        return new(
            null,
            text,
            ServiceCategory.Other,
            string.Empty,
            Guid.Empty,
            string.Empty,
            0m,
            string.Empty,
            string.Empty,
            text);
    }

    /// <summary>
    /// Собирает пункт из строки прайса карточки.
    /// </summary>
    /// <param name="service">Услуга мастера с ценой и длительностью.</param>
    /// <returns>Пункт для списка и <c>Picker</c>.</returns>
    public static BarberServiceOption From(BarberProfileServiceResponse service)
    {
        ArgumentNullException.ThrowIfNull(service);

        return new BarberServiceOption(
            service.HairServiceId,
            service.Name,
            service.Category,
            BarberProfileText.CategoryText(service.Category),
            service.SpecializationId,
            service.SpecializationName,
            service.Price,
            BarberText.Price(service.Price),
            BarberText.Duration(service.DurationMinutes),
            service.Name);
    }
}
