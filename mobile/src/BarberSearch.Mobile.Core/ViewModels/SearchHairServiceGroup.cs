namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Группа услуг одной специализации для сгруппированного <c>CollectionView</c>.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет группу веба (<c>frontend/src/lib/service-groups.ts</c>): строки одной
/// специализации идут под общим заголовком, поэтому длинный справочник читается сверху вниз
/// без поиска подписи у каждой строки. Наследование от <see cref="List{T}"/> — требование MAUI:
/// источник с <c>IsGrouped="True"</c> перечисляет группы, а заголовок берёт из свойства
/// <see cref="Title"/>.
/// </para>
/// <para>
/// Группа собирается из готовых пунктов <see cref="SearchCatalogOption"/> и не меняет их:
/// раскладку делает чистая функция <see cref="SearchServiceGroups.Build"/>.
/// </para>
/// </remarks>
public sealed class SearchHairServiceGroup : List<SearchCatalogOption>
{
    /// <summary>
    /// Создаёт группу с заголовком.
    /// </summary>
    /// <param name="title">Заголовок группы: название специализации или «Прочие услуги».</param>
    /// <param name="services">Услуги группы в порядке справочника.</param>
    public SearchHairServiceGroup(string title, IEnumerable<SearchCatalogOption> services)
        : base(services)
    {
        Title = title;
    }

    /// <summary>
    /// Заголовок группы в списке.
    /// </summary>
    public string Title { get; }
}
