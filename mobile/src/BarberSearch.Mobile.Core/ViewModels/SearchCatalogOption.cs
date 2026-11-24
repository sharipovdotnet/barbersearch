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
    private SearchCatalogOption(Guid? id, string displayText, Guid? specializationId)
    {
        Id = id;
        DisplayText = displayText;
        SpecializationId = specializationId;
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
    /// Специализация строки-услуги; <c>null</c> у пункта «Любая услуга» и у строк
    /// специализаций.
    /// </summary>
    /// <remarks>
    /// Нужна, чтобы фильтр «Услуга» сужался выбранной специализацией, как в вебе: у услуги
    /// справочника ровно одна специализация, и по этому полю экран понимает, осталась ли
    /// выбранная услуга в новом списке.
    /// </remarks>
    public Guid? SpecializationId { get; }

    /// <summary>
    /// Пункт «фильтра нет» с заданной подписью.
    /// </summary>
    /// <param name="text">Подпись пункта.</param>
    /// <returns>Пункт без идентификатора.</returns>
    public static SearchCatalogOption Any(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SearchCatalogOption(null, text, null);
    }

    /// <summary>
    /// Строка справочника: идентификатор и название.
    /// </summary>
    /// <param name="id">Идентификатор строки справочника.</param>
    /// <param name="name">Название для подписи.</param>
    /// <param name="specializationId">
    /// Специализация строки-услуги; <c>null</c> — строка специализации или пункт «любая».
    /// </param>
    /// <returns>Пункт для <c>Picker</c>.</returns>
    public static SearchCatalogOption From(Guid id, string name, Guid? specializationId = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new SearchCatalogOption(id, name, specializationId);
    }
}
