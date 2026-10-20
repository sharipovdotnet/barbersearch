using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
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
    /// Корень профиля, заблокированный для пересчёта рейтинга (<c>SELECT … FOR UPDATE</c>).
    /// </summary>
    /// <remarks>
    /// Дочерние коллекции не загружаются: для <c>UpdateRating</c> нужен только корень.
    /// Блокировка снимается при завершении транзакции, поэтому метод обязан вызываться
    /// внутри <c>IUnitOfWork</c>. Он сериализует конкурентные пересчёты одного профиля:
    /// второй писатель ждёт коммита первого и читает сводку уже с его изменениями.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetForRatingUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

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
    /// Страница поиска активных и подтверждённых барберов.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания и без Include коллекций: фильтры выражаются через EXISTS,
    /// а результат сразу проецируется в <see cref="BarberSearchItemResponse"/>.
    /// Порядок — <c>rating DESC NULLS LAST, id</c>; keyset-условие берётся из
    /// <see cref="BarberSearchFilter.Cursor"/>.
    /// </remarks>
    /// <param name="filter">Фильтры, позиция курсора и лимит строк.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<BarberSearchItemResponse>, Failure>> SearchAsync(
        BarberSearchFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Карточка активного и подтверждённого барбера: профиль, услуги, салоны и специализации.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания. Если профиля нет, он неактивен или не подтверждён —
    /// <c>Error.NotFound</c>; доменный граф наружу не отдаётся.
    /// </remarks>
    /// <param name="id">Идентификатор профиля барбера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberProfileResponse, Failure>> GetCardByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Данные для расчёта слотов: выбранный активный салон, его таймзона, расписание,
    /// длительность услуги и отпуска.
    /// </summary>
    /// <remarks>
    /// Профиль должен быть активным и подтверждённым. Если <paramref name="salonId"/>
    /// не задан, выбирается основной активный салон; если услуга не задана — длительность
    /// 30 минут. Отсутствие профиля, салона или услуги — <c>Error.NotFound</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="salonId">Салон или <c>null</c> для основного.</param>
    /// <param name="serviceId">Услуга каталога или <c>null</c> для шага 30 минут.</param>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberSlotContext, Failure>> GetSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? serviceId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Услуга, предложенная конкретным профилем барбера.
    /// </summary>
    /// <remarks>
    /// Узкое чтение для пути создания брони: достаточно строки <c>barber_services</c>, а не
    /// всего агрегата с пятью коллекциями. Неактивная услуга возвращается как есть — решение
    /// «можно ли по ней бронировать» принимает Application. Если услуга не принадлежит
    /// профилю, возвращается <c>Error.NotFound</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="barberServiceId">Идентификатор услуги барбера (<c>BarberService.Id</c>).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberService, Failure>> GetServiceAsync(
        Guid barberProfileId,
        Guid barberServiceId,
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
