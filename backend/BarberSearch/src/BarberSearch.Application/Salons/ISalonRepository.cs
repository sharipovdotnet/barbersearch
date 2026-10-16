using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Salons;

/// <summary>
/// Доступ к агрегату <see cref="Salon"/>: чтение и запись.
/// </summary>
public interface ISalonRepository
{
    /// <summary>
    /// Салон по идентификатору.
    /// </summary>
    Task<Result<Salon, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница салонов города (фильтр поиска по городу). Читается без отслеживания.
    /// </summary>
    /// <param name="city">
    /// Город. Сравнение точное (с обрезкой пробелов), чтобы работал индекс
    /// <c>(city, is_active)</c>; нормализация регистра — задача слоя выше.
    /// </param>
    Task<Result<IReadOnlyList<Salon>, Failure>> GetByCityAsync(
        string city,
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница салонов: по городу, затем по названию. Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<Salon>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Существует ли салон с таким идентификатором.
    /// </summary>
    Task<Result<bool, Failure>> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новый салон.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(Salon salon, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения салона.
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(Salon salon, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет салон.
    /// </summary>
    Task<UnitResult<Failure>> DeleteAsync(Salon salon, CancellationToken cancellationToken = default);
}
