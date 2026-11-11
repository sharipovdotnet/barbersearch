using BarberSearch.Domain.EmailChanges;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.EmailChanges;

/// <summary>
/// Доступ к одноразовым токенам смены адреса электронной почты.
/// </summary>
/// <remarks>
/// Открытый токен через этот интерфейс не ходит: приложение передаёт только SHA-256,
/// а реализация в Infrastructure хранит ровно его. Поиск по неизвестному хешу —
/// <c>Error.NotFound</c>; ошибки ограничений БД — <c>Error.Conflict</c> / <c>Error.Validation</c>.
/// </remarks>
public interface IEmailChangeTokenRepository
{
    /// <summary>
    /// Токен по хешу. Возвращённая сущность отслеживается контекстом, поэтому её изменение
    /// достаточно передать в <see cref="UpdateAsync"/>.
    /// </summary>
    /// <param name="tokenHash">SHA-256 в нижнем регистре.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<EmailChangeToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Активный токен пользователя либо <c>null</c>, если активного нет.
    /// </summary>
    /// <remarks>
    /// «Активный» — не использованный и не отозванный. Частичный уникальный индекс
    /// <c>ux_email_change_tokens_active</c> гарантирует, что такая строка не больше одной.
    /// </remarks>
    /// <param name="userId">Владелец токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<EmailChangeToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новый токен.
    /// </summary>
    /// <remarks>
    /// Нарушение частичного уникального индекса активных строк превращается в
    /// <c>409 auth.emailChange.cooldown</c> — это проигранная гонка двух заявок, а не сбой
    /// сервера. Нарушение уникальности хеша остаётся <c>500</c>: коллизия CSPRNG.
    /// </remarks>
    Task<UnitResult<Failure>> AddAsync(
        EmailChangeToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменение токена (отзыв при новой заявке или потребление при подтверждении).
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(
        EmailChangeToken token,
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
