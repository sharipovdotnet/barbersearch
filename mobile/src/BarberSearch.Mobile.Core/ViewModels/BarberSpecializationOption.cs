using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Специализация мастера: название из справочника, описание и признак открытой подсказки.
/// </summary>
/// <remarks>
/// <para>
/// Порядок — серверный (по имени справочника): карточка его не пересортировывает, чтобы
/// телефон и веб показывали одинаковый список.
/// </para>
/// <para>
/// На телефоне hover нет, поэтому веб-подсказка заменена тапом: состояние «описание открыто»
/// живёт на самом пункте, как <see cref="BarberSlotOption.IsSelected"/> на чипе слота.
/// Второй коллекции описаний нет — текст берётся из этого же пункта.
/// </para>
/// </remarks>
public sealed partial class BarberSpecializationOption : ObservableObject
{
    private BarberSpecializationOption(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>
    /// Название специализации.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Описание из справочника; <c>null</c> — описания нет.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Есть ли описание для подсказки: без него чип статичен, а панель описания не нужна.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// Открыта ли у этого чипа подсказка с описанием.
    /// </summary>
    /// <remarks>
    /// Выделение одно на весь список: прежний пункт снимает владелец коллекции
    /// (<see cref="BarberCardViewModel"/>), а не сам пункт.
    /// </remarks>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Собирает пункт из ответа карточки.
    /// </summary>
    /// <param name="specialization">Специализация мастера.</param>
    /// <returns>Пункт списка «Специализации».</returns>
    public static BarberSpecializationOption From(BarberProfileSpecializationResponse specialization)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        return new BarberSpecializationOption(specialization.Name, specialization.Description);
    }
}
