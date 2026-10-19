using BarberSearch.Domain.BarberProfiles;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles;

/// <summary>
/// Доступ к агрегату <see cref="BarberProfile"/>: чтение и запись.
/// </summary>
/// <remarks>
/// Профиль барбера — самый крупный агрегат: у него пять дочерних коллекций
/// (салоны с расписанием, услуги, специализации, портфолио, отпуска).
/// Методы <c>GetByIdAsync</c> / <c>GetByUserIdAsync</c> грузят агрегат целиком (split-запрос),
/// а <c>GetByIdsAsync</c> и <c>GetPagedAsync</c> — только корни: их используют списки и поиск,
/// где коллекции не нужны (см. AGENT.md §15.6).
/// </remarks>
public interface IBarberProfileRepository
{
    /// <summary>
    /// Профиль со всеми дочерними коллекциями. Сущность отслеживается контекстом.
    /// </summary>
    Task<Result<BarberProfile, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Профиль барбера, принадлежащий пользователю, со всеми дочерними коллекциями.
    /// </summary>
    Task<Result<BarberProfile, Failure>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Корни профилей по списку идентификаторов, без дочерних коллекций.
    /// Порядок результата не гарантирован. Пустой список — пустой результат, а не ошибка.
    /// </summary>
    Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница профилей для выдачи: сначала с рейтингом, затем «без отзывов»,
    /// внутри группы — по убыванию рейтинга. Читается без отслеживания.
    /// </summary>
    /// <param name="page">Номер страницы, начиная с 1.</param>
    /// <param name="pageSize">Размер страницы, не больше 200.</param>
    /// <param name="onlyActive">Только активные профили (для публичной выдачи — <c>true</c>).</param>
    Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Существует ли профиль с таким идентификатором.
    /// </summary>
    Task<Result<bool, Failure>> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Принадлежит ли профиль пользователю. Проверка существования, без загрузки агрегата
    /// и его коллекций: используется при проверке прав барбера на бронь.
    /// </summary>
    Task<Result<bool, Failure>> IsOwnedByAsync(
        Guid barberProfileId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новый профиль вместе с дочерними коллекциями.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(BarberProfile barberProfile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения агрегата: правки корня, добавленные и удалённые дочерние сущности.
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(BarberProfile barberProfile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет профиль. Дочерние сущности уходят каскадом на уровне БД.
    /// </summary>
    Task<UnitResult<Failure>> DeleteAsync(BarberProfile barberProfile, CancellationToken cancellationToken = default);
}
