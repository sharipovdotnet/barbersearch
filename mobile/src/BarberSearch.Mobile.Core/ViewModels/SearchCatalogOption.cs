namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Пункт фильтра-справочника: специализация или услуга.
/// </summary>
/// <remarks>
/// Один тип на два селекта: поиск принимает и <c>specializationId</c>, и <c>hairServiceId</c>
/// одинаково — идентификатором справочника либо «любым значением». <c>null</c> в
/// <see cref="Id"/> — пункт «Любая специализация» / «Любая услуга»; такой фильтр в запрос
/// не попадает.
/// </remarks>
public sealed class SearchCatalogOption
{
    private SearchCatalogOption(Guid? id, string displayText)
    {
        Id = id;
        DisplayText = displayText;
    }

    /// <summary>
    /// Идентификатор строки справочника; <c>null</c> — «фильтра нет».
    /// </summary>
    public Guid? Id { get; }

    /// <summary>
    /// Подпись в <c>Picker</c>: название строки справочника или «Любая …».
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Пункт «фильтра нет» с заданной подписью.
    /// </summary>
    /// <param name="text">Подпись пункта.</param>
    /// <returns>Пункт без идентификатора.</returns>
    public static SearchCatalogOption Any(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SearchCatalogOption(null, text);
    }

    /// <summary>
    /// Строка справочника: идентификатор и название.
    /// </summary>
    /// <param name="id">Идентификатор строки справочника.</param>
    /// <param name="name">Название для подписи.</param>
    /// <returns>Пункт для <c>Picker</c>.</returns>
    public static SearchCatalogOption From(Guid id, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new SearchCatalogOption(id, name);
    }
}
