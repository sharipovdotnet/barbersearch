namespace BarberSearch.Application.Admin;

/// <summary>
/// Чтение админской статистики: cross-aggregate агрегаты по броням, клиентам,
/// профилям, салонам, услугам, отзывам и заявкам.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный репозиторий, как у сводки: статистика читает несколько агрегатов сразу
/// и не принадлежит ни одному из них. EF в Application не протекает — интерфейс
/// возвращает чистые записи, а SQL живёт в Infrastructure.Postgresql.
/// </para>
/// <para>
/// Все чтения последовательные: <c>DbContext</c> не потокобезопасен, поэтому
/// <c>Task.WhenAll</c> здесь запрещён. Репозиторий не решает, что показать при
/// <c>compare=false</c>, — он всегда считает предыдущий период, а обработчик прячет его.
/// </para>
/// </remarks>
public interface IAdminStatisticsRepository
{
    /// <summary>
    /// Считает все секции статистики за период одним последовательным проходом.
    /// </summary>
    /// <param name="filters">Проверенный период, предыдущий период и момент расчёта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сырые агрегаты без округления, долей и хвоста «Остальные».</returns>
    Task<AdminStatisticsSnapshot> ReadAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken);
}
