using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Строка справочника услуг в редакторе прайса.
/// </summary>
/// <remarks>
/// <para>
/// Выбор хранится на самой строке (<see cref="IsSelected"/>), а не только в редакторе: подсветка
/// выбранной услуги — это состояние строки шаблона, и без свойства ей нечего было бы показать,
/// кроме повторной подписи «Выбрано» где-то ниже списка. Редактор снимает флаг со всех строк,
/// когда выбор переезжает на другую услугу.
/// </para>
/// <para>
/// <see cref="MetaText"/> — вторая строка строки справочника и подпись выбранной услуги в карточке
/// цены: категория и длительность через « · », как в мета-строках кабинета и админки.
/// </para>
/// </remarks>
public sealed partial class BarberServiceCatalogItem : ObservableObject
{
    /// <summary>
    /// Создаёт строку из услуги справочника.
    /// </summary>
    /// <param name="service">Услуга из публичного справочника.</param>
    public BarberServiceCatalogItem(HairServiceResponse service)
    {
        ArgumentNullException.ThrowIfNull(service);

        Id = service.Id;
        Name = service.Name;
        CategoryText = BarberProfileText.CategoryText(service.Category);
        SpecializationId = service.SpecializationId;
        DefaultDurationMinutes = service.DefaultDurationMinutes;
    }

    /// <summary>
    /// Идентификатор услуги справочника.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Специализация услуги: по ней строка попадает в группу, а редактор добавляет её в профиль
    /// вместе с услугой.
    /// </summary>
    public Guid SpecializationId { get; }

    /// <summary>
    /// Название услуги.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Категория для показа: «Стрижка».
    /// </summary>
    public string CategoryText { get; }

    /// <summary>
    /// Длительность по умолчанию: её редактор подставляет в поле.
    /// </summary>
    public int DefaultDurationMinutes { get; }

    /// <summary>
    /// Длительность по умолчанию строкой: «45 мин».
    /// </summary>
    public string DurationText => BarberProfileText.DurationText(DefaultDurationMinutes);

    /// <summary>
    /// Вторая строка услуги: «Стрижка · 45 мин».
    /// </summary>
    public string MetaText => string.Join(" · ", CategoryText, DurationText);

    /// <summary>
    /// Услуга выбрана в справочнике: строка подсвечена рамкой и галочкой.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}
