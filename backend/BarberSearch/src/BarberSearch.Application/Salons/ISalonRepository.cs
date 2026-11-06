using BarberSearch.Application.Common;
using BarberSearch.Contracts.Salons.Admin;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
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
    /// Админский справочник салонов: полный список, включая выключенные строки, с фильтром
    /// статуса и поиском по названию и городу.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания; порядок — город, название, идентификатор. Поиск идёт через
    /// <c>ILIKE</c> с экранированием служебных символов шаблона. Страниц нет: справочник
    /// маленький и отдаётся целиком — до этого этапа у салонов не было ни одного чтения.
    /// </remarks>
    /// <param name="status">Фильтр статуса строки.</param>
    /// <param name="search">Поиск по названию или городу; <c>null</c> — без фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Строки справочника со всеми полями формы; <c>usageCount</c> — число активных
    /// подтверждённых профилей, работающих в салоне.
    /// </returns>
    Task<Result<IReadOnlyList<AdminSalonResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сколько салонов выключено — ровно <see cref="AdminCatalogueStatus.Hidden"/> для салонов.
    /// </summary>
    /// <remarks>
    /// Предикат — <c>is_active = false</c>: то же, что фильтр «Выключенные» админского каталога.
    /// Счётчик не делает join'ов: связи барберов с выключенным салоном сохраняются, и они
    /// не меняют ответ «сколько салонов не участвует в поиске».
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<int, Failure>> CountInactiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Салон по идентификатору.
    /// </summary>
    Task<Result<Salon, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Салоны по списку идентификаторов, без гарантии порядка. Пустой список — пустой результат.
    /// </summary>
    /// <remarks>
    /// Одно чтение вместо N+1 для экрана расписания: барбер работает в одном-двух салонах,
    /// но названия и часовые пояса нужны всем сразу.
    /// </remarks>
    Task<Result<IReadOnlyList<Salon>, Failure>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Публичный каталог активных салонов: фильтр по городу и поиск по названию и адресу.
    /// </summary>
    /// <remarks>
    /// Без пагинации: это справочник для выбора, как <c>GET api/specializations</c>.
    /// Читается без отслеживания; порядок — город, название, идентификатор. Поиск идёт
    /// через <c>ILIKE</c> с экранированием служебных символов шаблона.
    /// </remarks>
    /// <param name="city">Точный город после обрезки пробелов; <c>null</c> — без фильтра.</param>
    /// <param name="search">Поиск по названию или адресу; <c>null</c> — без фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<SalonDirectoryItemResponse>, Failure>> GetDirectoryAsync(
        string? city,
        string? search,
        CancellationToken cancellationToken = default);

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
