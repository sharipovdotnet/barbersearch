using BarberSearch.Application.BarberProfiles.Admin;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberSalons;
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
/// Write-пути не грузят граф целиком: <c>GetRootByIdAsync</c> читает только корень,
/// а методы <c>GetWith…Async</c> — корень и ровно ту коллекцию, которую меняет операция.
/// Полный граф с пятью <c>Include</c> не нужен ни одному обработчику (см. AGENT.md §15.6).
/// <c>GetByIdsAsync</c> и <c>GetPagedAsync</c> тоже читают только корни: их используют
/// списки и поиск, где коллекции не нужны.
/// </remarks>
public interface IBarberProfileRepository
{
    /// <summary>
    /// Корень профиля без дочерних коллекций. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Для операций, которые меняют только поля корня (активация, верификация, грейд,
    /// редактирование профиля): коллекции им не нужны, а их загрузка — лишние SQL-запросы.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetRootByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Корень профиля вместе с салонами, без расписания. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Для добавления, удаления и назначения основного салона: домен проверяет уникальность
    /// <c>SalonId</c> и единственность <c>IsPrimary</c> по коллекции салонов.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetWithSalonsAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Корень профиля с салонами и их расписанием. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Для операций над рабочими днями: инварианты расписания (уникальный день недели,
    /// границы времени) проверяет <c>BarberSalon</c>, поэтому нужны и салоны, и их расписание.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetWithSalonsAndSchedulesAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Корень профиля вместе с услугами. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Проверка дубликата <c>HairServiceId</c> и изменение одной услуги остаются в домене,
    /// но остальные четыре коллекции не загружаются.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetWithServicesAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Корень профиля вместе с портфолио. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Лимит фотографий и операции над одной фотографией проверяет агрегат; остальные
    /// коллекции для этого не нужны.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetWithPhotosAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Корень профиля вместе со специализациями. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Дубликат <c>SpecializationId</c> проверяет агрегат по этой коллекции.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetWithSpecializationsAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Корень профиля вместе с отпусками. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Пересечение периодов — инвариант агрегата, поэтому для <c>AddTimeOff</c> нужны
    /// существующие отпуска; услуги, салоны и портфолио не загружаются.
    /// </remarks>
    Task<Result<BarberProfile, Failure>> GetWithTimeOffsAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Идентификатор владельца профиля, без загрузки агрегата. Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Узкая проверка владения для запросов, которым сам профиль не нужен.
    /// </remarks>
    Task<Result<Guid, Failure>> GetOwnerIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Салон барбера вместе с расписанием пары (барбер, салон). Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Узкое чтение для <c>FindBarberProfileSalon</c>: остальные салоны и четыре другие
    /// коллекции профиля этому запросу не нужны.
    /// </remarks>
    Task<Result<BarberSalon, Failure>> GetSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default);

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
    /// <remarks>
    /// Глубина offset-выдачи ограничена: <c>page * pageSize</c> не может превышать 10 000,
    /// иначе <c>400</c> с кодом <c>barberProfile.paging.depth</c>. Проверка глубины выполняется
    /// после проверки параметров, поэтому при недопустимых <c>page</c>/<c>pageSize</c> клиент
    /// получает прежние <c>barberProfile.paging.page</c> / <c>barberProfile.paging.pageSize</c>.
    /// </remarks>
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
    /// <see cref="BarberSearchFilter.HairServiceId"/> — услуга справочника
    /// (<c>hair_services.id</c>), не строка «услуга барбера» (R-20).
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
    /// не задан, выбирается основной активный салон; если услуга не задана — шаг
    /// <c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c> (30 минут). Отсутствие профиля, салона
    /// или услуги — <c>Error.NotFound</c>.
    /// <paramref name="hairServiceId"/> — услуга справочника (<c>hair_services.id</c>),
    /// а не строка «услуга барбера»: длительность и <c>BarberServiceId</c> читаются
    /// из одной строки <c>barber_services</c> этой услуги (R-20).
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="salonId">Салон или <c>null</c> для основного.</param>
    /// <param name="hairServiceId">
    /// Услуга справочника или <c>null</c> для шага <c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c>
    /// (30 минут).
    /// </param>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberSlotContext, Failure>> GetSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? hairServiceId,
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
    /// Работает ли барбер в указанном салоне. Узкий <c>EXISTS</c> по <c>barber_salons</c>:
    /// проверка нужна при создании брони, чтобы <c>bookings.salon_id</c> указывал на салон,
    /// в котором барбер действительно работает.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<bool, Failure>> HasSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница админского списка барберов: поиск по имени и email, фильтр по статусу.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания и без Include коллекций: email берётся join'ом по владельцу
    /// профиля, счётчики готовности в списке не нужны. Порядок задаёт
    /// <paramref name="status"/>: очередь проверки — <c>created_at ASC, id</c>, остальные —
    /// <c>created_at DESC, id</c>. <paramref name="limit"/> — это pageSize + 1, поэтому
    /// <c>limit &lt;= 0</c> — <c>400 admin.barbers.page.limit</c>.
    /// </remarks>
    /// <param name="status">Фильтр: все, на проверке или скрытые.</param>
    /// <param name="search">Поиск по имени или email либо <c>null</c>.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<AdminBarberResponse>, Failure>> GetAdminPageAsync(
        AdminBarberStatus status,
        string? search,
        AdminBarberCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Карточка барбера любого статуса вместе с чек-листом готовности.
    /// </summary>
    /// <remarks>
    /// Публичное чтение <c>GetCardByIdAsync</c> отдаёт только активные и подтверждённые
    /// профили; админскому экрану нужны и остальные. Счётчики готовности считаются
    /// подзапросами, коллекции агрегата не загружаются.
    /// </remarks>
    /// <param name="id">Идентификатор профиля барбера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<AdminBarberDetailResponse, Failure>> GetAdminByIdAsync(
        Guid id,
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
