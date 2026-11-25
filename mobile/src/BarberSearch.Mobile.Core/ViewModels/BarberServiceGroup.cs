namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Группа услуг одной специализации в прайсе публичной карточки мастера.
/// </summary>
/// <remarks>
/// <para>
/// Специализация стоит заголовком группы, а под ним идут прежние строки прайса: бейдж
/// специализации на каждой строке повторялся и обрезался на узком экране, поэтому теперь
/// название читается один раз сверху — так же, как в фильтре «Услуга» на «Поиске».
/// </para>
/// <para>
/// Группа — неизменяемый снимок готовых строк: её собирает чистая функция
/// <see cref="BarberServiceGroups.Build"/>, а не сама строка прайса.
/// </para>
/// </remarks>
public sealed class BarberServiceGroup
{
    /// <summary>
    /// Создаёт группу услуг с заголовком.
    /// </summary>
    /// <param name="title">Заголовок группы: название специализации или «Прочие услуги».</param>
    /// <param name="services">Строки прайса в исходном порядке.</param>
    public BarberServiceGroup(string title, IReadOnlyList<BarberServiceOption> services)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(services);

        Title = title;
        Services = services;
    }

    /// <summary>
    /// Заголовок группы в списке.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Строки прайса этой специализации в порядке исходного списка.
    /// </summary>
    public IReadOnlyList<BarberServiceOption> Services { get; }
}
