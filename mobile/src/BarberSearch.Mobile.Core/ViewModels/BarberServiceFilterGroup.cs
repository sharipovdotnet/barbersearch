namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Группа услуг фильтра «Услуга» в карточке мастера для сгруппированного
/// <c>CollectionView</c>.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет группу веба (<c>frontend/src/lib/barber-service-groups.ts</c>): строки одной
/// специализации идут под общим заголовком, поэтому длинный прайс читается сверху вниз без
/// подписи специализации у каждой строки. Наследование от <see cref="List{T}"/> — требование
/// MAUI: источник с <c>IsGrouped="True"</c> перечисляет группы, а заголовок берёт из свойства
/// <see cref="Title"/>.
/// </para>
/// <para>
/// Группа собирается из готовых пунктов <see cref="BarberServiceOption"/> и не меняет их:
/// раскладку делает чистая функция <see cref="BarberServiceFilterGroups.Build"/>. Это не
/// <see cref="BarberServiceGroup"/> из прайса: тот не <see cref="IEnumerable{T}"/> и остаётся
/// источником для <c>BindableLayout</c> публичного прайса.
/// </para>
/// </remarks>
public sealed class BarberServiceFilterGroup : List<BarberServiceOption>
{
    /// <summary>
    /// Создаёт группу с заголовком.
    /// </summary>
    /// <param name="title">Заголовок группы: название специализации или «Прочие услуги».</param>
    /// <param name="services">Услуги группы в порядке ответа API.</param>
    public BarberServiceFilterGroup(string title, IEnumerable<BarberServiceOption> services)
        : base(services)
    {
        Title = title;
    }

    /// <summary>
    /// Заголовок группы в списке.
    /// </summary>
    public string Title { get; }
}
