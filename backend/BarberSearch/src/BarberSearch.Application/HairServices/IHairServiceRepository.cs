using BarberSearch.Application.Common;
using BarberSearch.Contracts.HairServices.Admin;
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
    /// Админский справочник услуг: полный список, включая выключенные строки, с фильтром
    /// статуса и поиском по названию.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания; порядок — категория, название, идентификатор. Поиск
    /// идёт через <c>ILIKE</c> с экранированием <c>%</c>, <c>_</c> и <c>\</c>, поэтому символы
    /// шаблона остаются литералами. Страниц нет: справочник маленький и отдаётся целиком,
    /// а созданная строка должна быть видна сразу.
    /// </remarks>
    /// <param name="status">Фильтр статуса строки.</param>
    /// <param name="search">Поиск по названию; <c>null</c> — без фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Строки справочника; <c>usageCount</c> — число активных подтверждённых профилей,
    /// у которых есть включённая связь с услугой.
    /// </returns>
    Task<Result<IReadOnlyList<AdminHairServiceResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default);

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
    /// Существует ли услуга с таким идентификатором. Узкая проверка ссылки:
    /// используется перед добавлением услуги в профиль барбера, чтобы не записать
    /// висячую ссылку на несуществующую строку <c>hair_services</c>.
    /// </summary>
    Task<Result<bool, Failure>> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

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
