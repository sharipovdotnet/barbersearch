using BarberSearch.Domain.Specializations;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Specializations;

/// <summary>
/// Доступ к агрегату <see cref="Specialization"/> — справочнику специализаций.
/// </summary>
public interface ISpecializationRepository
{
    /// <summary>
    /// Специализация по идентификатору.
    /// </summary>
    Task<Result<Specialization, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Все активные специализации справочника. Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<Specialization>, Failure>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Специализация по названию — защита от дублей при наполнении справочника.
    /// Сравнение без учёта регистра. Читается без отслеживания.
    /// </summary>
    Task<Result<Specialization, Failure>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница специализаций по названию. Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<Specialization>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новую специализацию.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(Specialization specialization, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения специализации.
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(Specialization specialization, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет специализацию из справочника.
    /// </summary>
    Task<UnitResult<Failure>> DeleteAsync(Specialization specialization, CancellationToken cancellationToken = default);
}
