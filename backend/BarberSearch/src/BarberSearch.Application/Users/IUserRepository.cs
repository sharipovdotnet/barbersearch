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
    /// Пользователь по адресу электронной почты (вход в систему).
    /// </summary>
    Task<Result<User, Failure>> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница пользователей, свежие сверху. Читается без отслеживания.
    /// </summary>
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
