namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Группа услуг одной специализации в прайсе кабинета мастера.
/// </summary>
/// <remarks>
/// <para>
/// Специализация стоит заголовком группы, а под ним идут прежние строки прайса с категорией,
/// длительностью, статусом и действиями: бейдж специализации на каждой строке повторялся и
/// обрезался на узком экране, поэтому название читается один раз сверху — так же, как в
/// фильтре «Услуга» на «Поиске». Скрытые услуги остаются в своих группах: кабинет показывает
/// прайс целиком.
/// </para>
/// <para>
/// Группа — неизменяемый снимок готовых строк: её собирает чистая функция
/// <see cref="BarberCabinetServiceGroups.Build"/>, а не сама строка прайса.
/// </para>
/// </remarks>
public sealed class BarberCabinetServiceGroup
{
    /// <summary>
    /// Создаёт группу услуг с заголовком.
    /// </summary>
    /// <param name="title">Заголовок группы: название специализации или «Прочие услуги».</param>
    /// <param name="services">Строки прайса в порядке ответа API.</param>
    public BarberCabinetServiceGroup(string title, IReadOnlyList<BarberServiceItem> services)
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
    /// Строки прайса этой специализации в порядке ответа API.
    /// </summary>
    public IReadOnlyList<BarberServiceItem> Services { get; }
}
