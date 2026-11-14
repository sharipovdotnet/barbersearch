using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Специализация барбера: название из справочника и описание.
/// </summary>
/// <remarks>
/// Порядок — серверный (по имени справочника): карточка его не пересортировывает, чтобы
/// телефон и веб показывали одинаковый список.
/// </remarks>
public sealed class BarberSpecializationOption
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
    /// Есть ли описание для показа под названием.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// Собирает пункт из ответа карточки.
    /// </summary>
    /// <param name="specialization">Специализация барбера.</param>
    /// <returns>Пункт списка «Специализации».</returns>
    public static BarberSpecializationOption From(BarberProfileSpecializationResponse specialization)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        return new BarberSpecializationOption(specialization.Name, specialization.Description);
    }
}
