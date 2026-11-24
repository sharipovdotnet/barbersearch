using BarberSearch.Application.Common;
using BarberSearch.Contracts.Specializations.Admin;
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
    /// Админский справочник специализаций: полный список, включая выключенные строки,
    /// с фильтром статуса и поиском по названию.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания; порядок — название, идентификатор. Поиск идёт через
    /// <c>ILIKE</c> с экранированием служебных символов шаблона. Страниц нет: справочник
    /// маленький и отдаётся целиком.
    /// </remarks>
    /// <param name="status">Фильтр статуса строки.</param>
    /// <param name="search">Поиск по названию; <c>null</c> — без фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Строки справочника; <c>usageCount</c> — число активных подтверждённых профилей,
    /// указавших специализацию.
    /// </returns>
    Task<Result<IReadOnlyList<AdminSpecializationResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default);

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
    /// Существует ли специализация с таким идентификатором. Узкая проверка ссылки:
    /// используется перед добавлением специализации в профиль барбера, чтобы не записать
    /// висячую ссылку на несуществующую строку <c>specializations</c>.
    /// </summary>
    Task<Result<bool, Failure>> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

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
