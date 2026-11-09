using BarberSearch.Domain.PasswordResets;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.PasswordResets;

/// <summary>
/// Доступ к одноразовым токенам сброса пароля.
/// </summary>
/// <remarks>
/// Открытый токен через этот интерфейс не ходит: приложение передаёт только SHA-256,
/// а реализация в Infrastructure хранит ровно его. Поиск по неизвестному хешу —
/// <c>Error.NotFound</c>; ошибки ограничений БД — <c>Error.Conflict</c> / <c>Error.Validation</c>.
/// </remarks>
public interface IPasswordResetTokenRepository
{
    /// <summary>
    /// Токен по хешу. Возвращённая сущность отслеживается контекстом, поэтому её изменение
    /// достаточно передать в <see cref="UpdateAsync"/>.
    /// </summary>
    /// <param name="tokenHash">SHA-256 в нижнем регистре.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<PasswordResetToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Активный токен пользователя либо <c>null</c>, если активного нет.
    /// </summary>
    /// <remarks>
    /// «Активный» — не использованный, не отозванный и не истёкший. Частичный уникальный
    /// индекс <c>ux_password_reset_tokens_active</c> гарантирует, что такая строка
    /// не больше одной.
    /// </remarks>
    /// <param name="userId">Владелец токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<PasswordResetToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новый токен.
    /// </summary>
    /// <remarks>
    /// Нарушение частичного уникального индекса активных строк превращается во внутренний
    /// <c>409 passwordResetToken.issue.conflict</c> — это проигранная гонка двух
    /// переотправок, а не сбой сервера. Обработчик <c>request</c> гасит его в тихий успех.
    /// Нарушение уникальности хеша остаётся <c>500</c>: коллизия CSPRNG.
    /// </remarks>
    Task<UnitResult<Failure>> AddAsync(
        PasswordResetToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменение токена (отзыв при переотправке или потребление при сбросе).
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(
        PasswordResetToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет строки, у которых <c>ExpiresAt</c> или <c>ConsumedAt</c> старше
    /// <c>now - retention</c>, и возвращает количество удалённых строк.
    /// </summary>
    /// <param name="now">Текущий момент по бизнес-часам.</param>
    /// <param name="retention">Срок хранения после истечения или потребления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<int> DeleteExpiredAsync(
        DateTimeOffset now,
        TimeSpan retention,
        CancellationToken cancellationToken = default);
}
