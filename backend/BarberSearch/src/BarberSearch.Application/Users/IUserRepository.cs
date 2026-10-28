using BarberSearch.Application.Users.Admin;
using BarberSearch.Contracts.Users.Admin;
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
    /// Страница админского списка пользователей: фильтры статуса и роли, поиск и keyset-курсор.
    /// </summary>
    /// <remarks>
    /// Список читается без отслеживания и всегда свежими сверху (<c>created_at DESC, id</c>).
    /// <paramref name="limit"/> — уже увеличенный на единицу размер страницы: репозиторий
    /// не решает, есть ли следующая страница, а возвращает на одну строку больше.
    /// </remarks>
    /// <param name="status">Фильтр статуса.</param>
    /// <param name="role">Фильтр роли либо <c>null</c> для всех ролей.</param>
    /// <param name="search">Поиск по имени, фамилии или email; <c>null</c> — без фильтра.</param>
    /// <param name="cursor">Позиция последней строки предыдущей страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<AdminUserResponse>, Failure>> GetAdminPageAsync(
        AdminUserStatus status,
        UserRole? role,
        string? search,
        AdminUserCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Админская карточка пользователя: контакты, доступ и привязанный профиль барбера.
    /// </summary>
    /// <remarks>
    /// Читается одним SQL-запросом с <c>LEFT JOIN</c> по профилю барбера. Отсутствие
    /// пользователя — <c>Error.NotFound</c> с кодом <c>admin.users.notFound</c>.
    /// </remarks>
    /// <param name="id">Идентификатор пользователя.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<AdminUserDetailResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сколько активных администраторов есть в системе.
    /// </summary>
    /// <remarks>
    /// Нужно правилу «нельзя оставить сервис без администратора». Читается внутри
    /// транзакции после <see cref="AcquireAdminLockAsync"/>: без блокировки два одновременных
    /// понижения могли бы оба увидеть двух администраторов и снять обоих.
    /// </remarks>
    Task<Result<int, Failure>> CountActiveAdminsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Берёт транзакционную блокировку на смену ролей и блокировку пользователей.
    /// </summary>
    /// <remarks>
    /// Это <c>pg_advisory_xact_lock</c> по постоянному ключу администраторов. Блокировка
    /// снимается концом транзакции, поэтому метод обязан вызываться внутри
    /// <c>IUnitOfWork.ExecuteAsync</c>. Сериализуются только редкие операции с ролями,
    /// а не все записи пользователей.
    /// </remarks>
    Task<UnitResult<Failure>> AcquireAdminLockAsync(CancellationToken cancellationToken = default);

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
