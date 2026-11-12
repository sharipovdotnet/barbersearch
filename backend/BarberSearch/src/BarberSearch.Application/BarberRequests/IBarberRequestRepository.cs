using BarberSearch.Application.BarberRequests.Admin;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Domain.BarberRequests;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberRequests;

/// <summary>
/// Доступ к агрегату <see cref="BarberRequest"/>: чтение и запись.
/// </summary>
/// <remarks>
/// <para>
/// Абстракция живёт в Application, реализация — в Infrastructure (PostgreSQL).
/// Отсутствие заявки в <see cref="GetByIdAsync"/> — <c>Error.NotFound</c> с публичным кодом
/// <c>barberRequest.notFound</c>; у <see cref="GetLatestByUserAsync"/> отсутствие заявки —
/// это <c>null</c>, а не ошибка.
/// </para>
/// <para>
/// Заявка не входит в allowlist <c>audit_log</c> (R-34): смена роли уже пишется как
/// <c>user.updated</c>, а город и «о себе» — персональные данные, которым не место в журнале.
/// Репозиторий поэтому не расширяет аудит; причина записана и в конфигурации таблицы.
/// </para>
/// </remarks>
public interface IBarberRequestRepository
{
    /// <summary>
    /// Заявка по идентификатору. Сущность отслеживается контекстом, поэтому её изменения
    /// достаточно передать в <see cref="UpdateAsync"/>.
    /// </summary>
    /// <param name="id">Идентификатор заявки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberRequest, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Последняя заявка пользователя в любом статусе; <c>null</c> — заявок ещё не было.
    /// </summary>
    /// <remarks>
    /// Порядок <c>created_at DESC, id DESC</c>: после отказа клиент может подать заявку снова,
    /// и кабинет должен показывать актуальную, а не первую. Читается без отслеживания.
    /// </remarks>
    /// <param name="userId">Заявитель.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberRequestResponse?, Failure>> GetLatestByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Есть ли у пользователя заявка в статусе <c>Pending</c>.
    /// </summary>
    /// <remarks>
    /// Предпроверка перед вставкой, чтобы не ловить конфликт уникального индекса на обычном
    /// пути. Гонку двух подач закрывает частичный уникальный индекс
    /// <c>ux_barber_requests_pending_user</c>, а обработчик переводит его ошибку в тот же
    /// публичный код <c>barberRequest.create.alreadyPending</c>.
    /// </remarks>
    /// <param name="userId">Заявитель.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<bool, Failure>> HasPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница заявок для администратора: очередь <c>pending</c> или все заявки.
    /// </summary>
    /// <remarks>
    /// <c>Pending</c> читается в порядке <c>created_at ASC, id</c> (FIFO: старые сверху),
    /// <c>All</c> — <c>created_at DESC, id</c> (свежие сверху). Направление задаёт статус,
    /// курсор хранит общую пару <c>(created_at, id)</c>. Проекция сразу собирает
    /// администраторский ответ с заявителем и PII, поэтому доступ закрыт <c>AdminOnly</c>.
    /// <paramref name="limit"/> — это <c>pageSize + 1</c>, поэтому <c>limit &lt;= 0</c> —
    /// <c>400 barberRequest.page.limit</c>.
    /// </remarks>
    /// <param name="status">Фильтр статуса: <c>Pending</c> или <c>All</c>.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<AdminBarberRequestResponse>, Failure>> GetAdminPageAsync(
        AdminBarberRequestStatus status,
        AdminBarberRequestCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сколько заявок ждёт решения: ровно очередь <c>AdminBarberRequestStatus.Pending</c>.
    /// </summary>
    /// <remarks>
    /// Предикат <c>status = Pending</c> совпадает с фильтром раздела — это контракт,
    /// а не деталь реализации: очередь по умолчанию у <c>GET api/admin/barber-requests</c>
    /// и счётчик сводки обязаны показывать одно число. Одобренные и отклонённые заявки
    /// в очередь не входят.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<int, Failure>> CountPendingAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Одна заявка любого статуса для администраторской карточки.
    /// </summary>
    /// <param name="id">Идентификатор заявки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<AdminBarberRequestResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет заявку. Повторная активная заявка ловится частичным уникальным индексом
    /// <c>ux_barber_requests_pending_user</c> и приходит как
    /// <c>barberRequest.add.uniqueViolation</c>; обработчик переводит её в
    /// <c>barberRequest.create.alreadyPending</c>.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(
        BarberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения заявки (одобрение или отклонение).
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(
        BarberRequest request,
        CancellationToken cancellationToken = default);
}
