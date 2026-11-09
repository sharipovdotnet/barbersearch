using BarberSearch.Application.PasswordResets;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.PasswordResets;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий одноразовых токенов сброса пароля. Открытые токены сюда не попадают —
/// только SHA-256.
/// </summary>
/// <remarks>
/// Нарушение частичного уникального индекса активных строк — не сбой сервера, а проигранная
/// гонка двух переотправок: возвращается внутренний <c>409 passwordResetToken.issue.conflict</c>,
/// который обработчик <c>request</c> гасит в тихий успех. Нарушение уникальности хеша —
/// коллизия CSPRNG, она остаётся <c>500 internal.database</c>.
/// </remarks>
public sealed class PasswordResetTokenRepository
    : RepositoryBase<PasswordResetToken>, IPasswordResetTokenRepository
{
    /// <summary>
    /// Имя частичного уникального индекса активных строк.
    /// </summary>
    public const string ACTIVE_INDEX_NAME = "ux_password_reset_tokens_active";

    /// <summary>
    /// Имя уникального индекса хеша.
    /// </summary>
    public const string HASH_INDEX_NAME = "ux_password_reset_tokens_hash";

    /// <summary>
    /// Код ошибки «токена с таким хешем нет».
    /// </summary>
    public const string NOT_FOUND_CODE = "passwordResetToken.notFound";

    /// <summary>
    /// Код ошибки «хеш токена пуст».
    /// </summary>
    public const string INVALID_HASH_CODE = "passwordResetToken.tokenHash.invalid";

    /// <summary>
    /// Внутренний код проигранной гонки за активную строку. Публичным контрактом не является:
    /// обработчик <c>request</c> превращает его в тихий успех, наружу код не выходит.
    /// </summary>
    public const string ISSUE_CONFLICT_CODE = "passwordResetToken.issue.conflict";

    /// <summary>
    /// Префикс кодов остальных ошибок сохранения; публичным контрактом не является.
    /// </summary>
    private const string SAVE_ERROR_PREFIX = "auth.passwordReset";

    public PasswordResetTokenRepository(
        ApplicationDbContext context,
        AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    /// <inheritdoc />
    public async Task<Result<PasswordResetToken, Failure>> GetByHashAsync(
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

        var token = await Context.PasswordResetTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            return new Failure(Error.NotFound(
                NOT_FOUND_CODE,
                "Password reset token was not found."));
        }

        return token;
    }

    /// <inheritdoc />
    public async Task<Result<PasswordResetToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Предикат совпадает с фильтром частичного уникального индекса: строка «активна»,
        // пока не использована и не отозвана. Истёкшая, но не погашенная строка тоже
        // попадает сюда — переотправка обязана её отозвать, иначе индекс не пустит новую.
        var token = await Context.PasswordResetTokens
            .Where(x => x.UserId == userId
                        && x.ConsumedAt == null
                        && x.RevokedAt == null)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return token;
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> AddAsync(
        PasswordResetToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        await Context.PasswordResetTokens.AddAsync(token, cancellationToken);

        return await SaveTokenChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> UpdateAsync(
        PasswordResetToken token,
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
        // кулдаун. Сравнение строгое: ровно на границе ретенции строка ещё хранится.
        var deleted = await Context.PasswordResetTokens
            .Where(x => x.ExpiresAt < cutoff
                        || (x.ConsumedAt != null && x.ConsumedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete идёт мимо change tracker: удалённые строки могли остаться
        // отслеживаемыми, и следующий SaveChanges попытался бы их обновить.
        DetachTracked(token => IsExpired(token, cutoff));

        return deleted;
    }

    /// <summary>
    /// Предикат уборки: строка старше границы ретенции по сроку истечения или потребления.
    /// </summary>
    /// <remarks>
    /// Отдельный метод, а не дважды записанное выражение: тот же предикат снимает удалённые
    /// строки с change tracker, и его граница проверяется unit-тестом без базы.
    /// </remarks>
    internal static bool IsExpired(PasswordResetToken token, DateTimeOffset cutoff)
        => token.ExpiresAt < cutoff
           || (token.ConsumedAt is not null && token.ConsumedAt < cutoff);

    /// <summary>
    /// Сохраняет изменения и переводит ограничения PostgreSQL в ошибки.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="RepositoryBase{TEntity}.SaveChangesAsync"/>: только здесь
    /// известно, что именно нарушение <c>ux_password_reset_tokens_active</c> означает
    /// проигранную гонку переотправки. Хеш-коллизия и остальные ошибки идут общим путём.
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
                "passwordResetToken.update.concurrencyConflict",
                $"The password reset token was modified by another request ({exception.Entries.Count} entries).")));
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
                ISSUE_CONFLICT_CODE,
                "Another active password reset token was issued concurrently.")));
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == HASH_INDEX_NAME)
        {
            // Коллизия SHA-256 двух случайных токенов — не бизнес-ситуация.
            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                "Password reset token hash collision.")));
        }

        return UnitResult.Failure(MapPostgresError(exception, SAVE_ERROR_PREFIX));
    }
}
