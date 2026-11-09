using BarberSearch.Application.EmailVerifications;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailVerifications;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий одноразовых токенов подтверждения email. Открытые токены сюда не попадают —
/// только SHA-256.
/// </summary>
/// <remarks>
/// Нарушение частичного уникального индекса активных строк — не сбой сервера, а проигранная
/// гонка двух переотправок: возвращается <c>409 auth.emailVerification.cooldown</c>. Нарушение
/// уникальности хеша — коллизия CSPRNG, она остаётся <c>500 internal.database</c>.
/// </remarks>
public sealed class EmailVerificationTokenRepository
    : RepositoryBase<EmailVerificationToken>, IEmailVerificationTokenRepository
{
    /// <summary>
    /// Имя частичного уникального индекса активных строк.
    /// </summary>
    public const string ACTIVE_INDEX_NAME = "ux_email_verification_tokens_active";

    /// <summary>
    /// Имя уникального индекса хеша.
    /// </summary>
    public const string HASH_INDEX_NAME = "ux_email_verification_tokens_hash";

    /// <summary>
    /// Код ошибки «токена с таким хешем нет».
    /// </summary>
    public const string NOT_FOUND_CODE = "emailVerificationToken.notFound";

    /// <summary>
    /// Код ошибки «хеш токена пуст».
    /// </summary>
    public const string INVALID_HASH_CODE = "emailVerificationToken.tokenHash.invalid";

    /// <summary>
    /// Префикс кодов остальных ошибок сохранения; публичным контрактом не является.
    /// </summary>
    private const string SAVE_ERROR_PREFIX = "auth.emailVerification";

    public EmailVerificationTokenRepository(
        ApplicationDbContext context,
        AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    /// <inheritdoc />
    public async Task<Result<EmailVerificationToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return new Failure(Error.Validation(
                INVALID_HASH_CODE,
                "Token hash cannot be empty.",
                nameof(tokenHash)));
        }

        var token = await Context.EmailVerificationTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            return new Failure(Error.NotFound(
                NOT_FOUND_CODE,
                "Email verification token was not found."));
        }

        return token;
    }

    /// <inheritdoc />
    public async Task<Result<EmailVerificationToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Предикат совпадает с фильтром частичного уникального индекса: строка «активна»,
        // пока не использована и не отозвана. Истёкшая, но не погашенная строка тоже
        // попадает сюда — переотправка обязана её отозвать, иначе индекс не пустит новую.
        var token = await Context.EmailVerificationTokens
            .Where(x => x.UserId == userId
                        && x.ConsumedAt == null
                        && x.RevokedAt == null)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return token;
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> AddAsync(
        EmailVerificationToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        await Context.EmailVerificationTokens.AddAsync(token, cancellationToken);

        return await SaveTokenChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> UpdateAsync(
        EmailVerificationToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (Context.Entry(token).State == EntityState.Detached)
        {
            Context.Update(token);
        }

        return await SaveTokenChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset now,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        var cutoff = now - retention;

        // Удаляются только строки, у которых истёк срок или которые использованы раньше
        // границы. Активная строка и недавняя использованная остаются: по ним считается
        // кулдаун и повторный клик по ссылке.
        var deleted = await Context.EmailVerificationTokens
            .Where(x => x.ExpiresAt < cutoff
                        || (x.ConsumedAt != null && x.ConsumedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete идёт мимо change tracker: удалённые строки могли остаться
        // отслеживаемыми, и следующий SaveChanges попытался бы их обновить.
        DetachTracked(token =>
            token.ExpiresAt < cutoff
            || (token.ConsumedAt != null && token.ConsumedAt < cutoff));

        return deleted;
    }

    /// <summary>
    /// Сохраняет изменения и переводит ограничения PostgreSQL в доменные ошибки.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="RepositoryBase{TEntity}.SaveChangesAsync"/>: только здесь
    /// известно, что именно нарушение <c>ux_email_verification_tokens_active</c> означает
    /// кулдаун. Хеш-коллизия и остальные ошибки идут общим путём.
    /// </remarks>
    private async Task<UnitResult<Failure>> SaveTokenChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Context.SaveChangesAsync(cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (PostgresException exception)
        {
            return FailAndClear(exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            Context.ChangeTracker.Clear();

            return UnitResult.Failure(new Failure(Error.Conflict(
                "emailVerificationToken.update.concurrencyConflict",
                $"The email verification token was modified by another request ({exception.Entries.Count} entries).")));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres)
        {
            return FailAndClear(postgres);
        }
        catch (DbUpdateException exception)
        {
            Context.ChangeTracker.Clear();

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                exception.Message)));
        }
    }

    private UnitResult<Failure> FailAndClear(PostgresException exception)
    {
        // После Failure контекст пуст — тот же контракт, что у RepositoryBase.
        Context.ChangeTracker.Clear();

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == ACTIVE_INDEX_NAME)
        {
            return UnitResult.Failure(new Failure(Error.Conflict(
                ApiErrorCodes.Auth.EmailVerificationCooldown,
                "Another active email verification token was issued concurrently.")));
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == HASH_INDEX_NAME)
        {
            // Коллизия SHA-256 двух случайных токенов — не бизнес-ситуация.
            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                "Email verification token hash collision.")));
        }

        return UnitResult.Failure(MapPostgresError(exception, SAVE_ERROR_PREFIX));
    }
}
