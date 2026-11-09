using BarberSearch.Domain.EmailVerifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.EmailVerifications;

/// <summary>
/// Доступ к одноразовым токенам подтверждения email.
/// </summary>
/// <remarks>
/// Открытый токен через этот интерфейс не ходит: приложение передаёт только SHA-256,
/// а реализация в Infrastructure хранит ровно его. Поиск по неизвестному хешу —
/// <c>Error.NotFound</c>; ошибки ограничений БД — <c>Error.Conflict</c> / <c>Error.Validation</c>.
/// </remarks>
public interface IEmailVerificationTokenRepository
{
    /// <summary>
    /// Токен по хешу. Возвращённая сущность отслеживается контекстом, поэтому её изменение
    /// достаточно передать в <see cref="UpdateAsync"/>.
    /// </summary>
    /// <param name="tokenHash">SHA-256 в нижнем регистре.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<EmailVerificationToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Активный токен пользователя либо <c>null</c>, если активного нет.
    /// </summary>
    /// <remarks>
    /// «Активный» — не использованный, не отозванный и не истёкший. Частичный уникальный
    /// индекс <c>ux_email_verification_tokens_active</c> гарантирует, что такая строка
    /// не больше одной.
    /// </remarks>
    /// <param name="userId">Владелец токена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<EmailVerificationToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новый токен.
    /// </summary>
    /// <remarks>
    /// Нарушение частичного уникального индекса активных строк превращается в
    /// <c>409 auth.emailVerification.cooldown</c> — это проигранная гонка двух переотправок,
    /// а не сбой сервера. Нарушение уникальности хеша остаётся <c>500</c>: коллизия CSPRNG.
    /// </remarks>
    Task<UnitResult<Failure>> AddAsync(
        EmailVerificationToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменение токена (отзыв при переотправке или потребление при подтверждении).
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(
        EmailVerificationToken token,
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
