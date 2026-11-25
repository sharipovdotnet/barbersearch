using System.Globalization;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Порядок услуг в публичном прайсе: категория → специализация → название.
/// </summary>
/// <remarks>
/// <para>
/// Эталон — <c>sortBarberServices</c> из <c>frontend/src/lib/barber-services-order.ts</c>:
/// категории идут каталожным порядком <see cref="SearchSpecializationGroups.CategoryOrder"/>
/// (стрижка, борода, окрашивание, укладка, уход, другое), а не по алфавиту — клиент ищет
/// знакомую услугу в привычном порядке витрины. Внутри категории услуги одной специализации
/// стоят рядом, поэтому бейджи читаются сверху вниз как группы. Специализации и названия
/// сравниваются русской локалью, <see cref="BarberProfileServiceResponse.BarberServiceId"/>
/// закрывает полное равенство: порядок не зависит от порядка ответа API и не «прыгает»
/// между запросами.
/// </para>
/// <para>
/// Функция чистая: возвращает новый список, входной не меняется. Неизвестная категория
/// оказывается в конце, а не в середине списка.
/// </para>
/// </remarks>
public static class BarberServicesOrder
{
    private static readonly CompareInfo Russian = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;

    private static readonly Dictionary<ServiceCategory, int> CategoryRanks = BuildRanks();

    /// <summary>
    /// Сравнивает две услуги в порядке публичного прайса.
    /// </summary>
    /// <param name="left">Первая услуга.</param>
    /// <param name="right">Вторая услуга.</param>
    /// <returns>Отрицательное число, ноль или положительное число.</returns>
    public static int Compare(
        BarberProfileServiceResponse left,
        BarberProfileServiceResponse right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var byCategory = CategoryRank(left.Category) - CategoryRank(right.Category);

        if (byCategory != 0)
        {
            return byCategory;
        }

        var bySpecialization = Russian.Compare(
            left.SpecializationName,
            right.SpecializationName,
            CompareOptions.None);

        if (bySpecialization != 0)
        {
            return bySpecialization;
        }

        var byName = Russian.Compare(left.Name, right.Name, CompareOptions.None);

        if (byName != 0)
        {
            return byName;
        }

        return left.BarberServiceId.CompareTo(right.BarberServiceId);
    }

    /// <summary>
    /// Раскладывает услуги в порядке публичного прайса.
    /// </summary>
    /// <param name="services">Услуги в порядке ответа API.</param>
    /// <returns>Новый список в порядке категория → специализация → название.</returns>
    public static List<BarberProfileServiceResponse> Sort(
        IEnumerable<BarberProfileServiceResponse> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var sorted = new List<BarberProfileServiceResponse>(services);
        sorted.Sort(Compare);

        return sorted;
    }

    private static int CategoryRank(ServiceCategory category) =>
        CategoryRanks.TryGetValue(category, out var rank)
            ? rank
            : SearchSpecializationGroups.CategoryOrder.Count;

    private static Dictionary<ServiceCategory, int> BuildRanks()
    {
        var order = SearchSpecializationGroups.CategoryOrder;
        var ranks = new Dictionary<ServiceCategory, int>(order.Count);

        for (var index = 0; index < order.Count; index++)
        {
            ranks.Add(order[index], index);
        }

        return ranks;
    }
}
