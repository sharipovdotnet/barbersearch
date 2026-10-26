using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Users;

/// <summary>
/// Доступ к агрегату <see cref="User"/>: чтение и запись.
/// </summary>
/// <remarks>
/// Абстракция живёт в Application, реализация — в Infrastructure (PostgreSQL).
/// Отсутствие записи — это <c>Error.NotFound</c>, а не <c>null</c>;
/// ошибки ограничений БД приходят как <c>Error.Conflict</c> / <c>Error.Validation</c>.
/// Эксплуатационные ошибки (нет соединения, таймаут) не упаковываются в <see cref="Failure"/>,
/// а выбрасываются исключением — их обрабатывает верхний слой.
/// </remarks>
public interface IUserRepository
{
    /// <summary>
    /// Пользователь по идентификатору вместе с избранными барберами.
    /// Возвращённая сущность отслеживается контекстом, поэтому её изменения достаточно
    /// передать в <see cref="UpdateAsync"/>.
    /// </summary>
    Task<Result<User, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Узкая проекция учётных данных: активность и <c>SecurityStamp</c>, без агрегата и избранного.
    /// </summary>
    /// <remarks>
    /// Используется проверкой security stamp на каждом аутентифицированном запросе (R-11):
    /// SQL — <c>SELECT is_active, security_stamp … WHERE id = …</c> без <c>Include</c> и
    /// без отслеживания. Отсутствие записи — <c>Error.NotFound</c> с кодом
    /// <c>user.securityStamp.notFound</c>.
    /// </remarks>
    /// <param name="id">Идентификатор пользователя.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<UserSecurityStamp, Failure>> GetSecurityStampAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Пользователь по адресу электронной почты (вход в систему).
    /// </summary>
    Task<Result<User, Failure>> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница пользователей, свежие сверху. Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Глубина offset-выдачи ограничена: <c>page * pageSize</c> не может превышать 10 000,
    /// иначе <c>400</c> с кодом <c>user.paging.depth</c>. Проверка глубины выполняется после
    /// проверки параметров, поэтому при недопустимых <c>page</c>/<c>pageSize</c> клиент получает
    /// прежние <c>user.paging.page</c> / <c>user.paging.pageSize</c>.
    /// </remarks>
    Task<Result<IReadOnlyList<User>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Занят ли адрес электронной почты. Проверка перед регистрацией, чтобы не ловить
    /// конфликт уникального индекса.
    /// </summary>
    Task<Result<bool, Failure>> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Существует ли пользователь с таким идентификатором. Лёгкая проверка без загрузки
    /// агрегата: нужна администратору при фильтрации чужого списка броней.
    /// </summary>
    /// <param name="id">Идентификатор пользователя.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<bool, Failure>> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет нового пользователя. Нарушение уникальности адреса — <c>Error.Conflict</c>.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения существующего пользователя.
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет пользователя. Избранные барберы уходят каскадом на уровне БД.
    /// </summary>
    Task<UnitResult<Failure>> DeleteAsync(User user, CancellationToken cancellationToken = default);
}
