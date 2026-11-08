namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Предложение: активные барберы, новые профили и салоны по типу места.
/// </summary>
/// <param name="ActiveBarbers">Активные и подтверждённые профили.</param>
/// <param name="WorkingBarbers">Из них с завершённым визитом в периоде.</param>
/// <param name="IdleBarbers">Активные без визитов в периоде.</param>
/// <param name="NewProfiles">Профили, созданные в периоде.</param>
/// <param name="SalonsByKind">Все салоны, включая выключенные, по типу места.</param>
public record AdminStatisticsSupplyResponse(
    int ActiveBarbers,
    int WorkingBarbers,
    int IdleBarbers,
    int NewProfiles,
    IReadOnlyList<AdminStatisticsSalonKindResponse> SalonsByKind);

/// <summary>
/// Салонный срез по типу места: видимые в каталоге и скрытые.
/// </summary>
/// <param name="Kind">Имя <c>PlaceKind</c>: <c>Salon</c>, <c>RentalStudio</c>, …</param>
/// <param name="Listed">Показаны в каталоге.</param>
/// <param name="Unlisted">Скрыты из каталога.</param>
public record AdminStatisticsSalonKindResponse(string Kind, int Listed, int Unlisted);
