using BarberSearch.Domain.HairServices;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Группа специализаций одной категории для сгруппированного <c>CollectionView</c>.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет <c>optgroup</c> веба (<c>frontend/src/lib/specialization-groups.ts</c>):
/// специализации одной категории идут под общим заголовком, поэтому длинный справочник
/// читается сверху вниз, как в селекте витрины. Наследование от <see cref="List{T}"/> —
/// требование MAUI: источник с <c>IsGrouped="True"</c> перечисляет группы, а заголовок
/// берёт из свойства <see cref="Title"/>.
/// </para>
/// <para>
/// Группа собирается из готовых пунктов <see cref="SearchCatalogOption"/> и не меняет их:
/// раскладку делает чистая функция <see cref="SearchSpecializationGroups.Build"/>.
/// </para>
/// </remarks>
public sealed class SearchSpecializationGroup : List<SearchCatalogOption>
{
    /// <summary>
    /// Создаёт группу с категорией и её подписью.
    /// </summary>
    /// <param name="category">Категория услуг группы.</param>
    /// <param name="title">Заголовок группы: «Стрижка», «Борода» и т. д.</param>
    /// <param name="specializations">Специализации группы в порядке ответа API.</param>
    public SearchSpecializationGroup(
        ServiceCategory category,
        string title,
        IEnumerable<SearchCatalogOption> specializations)
        : base(specializations)
    {
        Category = category;
        Title = title;
    }

    /// <summary>
    /// Категория услуг, к которой относятся строки группы.
    /// </summary>
    public ServiceCategory Category { get; }

    /// <summary>
    /// Заголовок группы в списке.
    /// </summary>
    public string Title { get; }
}
