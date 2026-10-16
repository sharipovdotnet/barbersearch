using BarberSearch.Domain.HairServices;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.HairServices;

/// <summary>
/// Доступ к агрегату <see cref="HairService"/> — справочнику услуг платформы.
/// </summary>
public interface IHairServiceRepository
{
    /// <summary>
    /// Услуга по идентификатору.
    /// </summary>
    Task<Result<HairService, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Все активные услуги справочника — для выпадающих списков и фильтров.
    /// Справочник небольшой, постраничная выдача ему не нужна. Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<HairService>, Failure>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Активные услуги категории. Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<HairService>, Failure>> GetByCategoryAsync(
        ServiceCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница услуг: по категории, затем по названию. Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<HairService>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новую услугу в справочник.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(HairService hairService, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения услуги.
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(HairService hairService, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет услугу из справочника. Если она уже предложена барберами, БД вернёт
    /// <c>Error.Conflict</c> (внешний ключ) — вместо удаления услугу деактивируют.
    /// </summary>
    Task<UnitResult<Failure>> DeleteAsync(HairService hairService, CancellationToken cancellationToken = default);
}
