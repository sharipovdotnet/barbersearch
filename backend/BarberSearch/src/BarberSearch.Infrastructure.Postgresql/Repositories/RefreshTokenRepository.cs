using BarberSearch.Application.RefreshTokens;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий refresh-токенов. Открытые токены сюда не попадают — только SHA-256.
/// </summary>
public sealed class RefreshTokenRepository : RepositoryBase<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<RefreshToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.RefreshToken.TokenHashInvalid,
                "Token hash cannot be empty.",
                nameof(tokenHash)));
        }

        var token = await Context.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.RefreshToken.NotFound,
                "Refresh token was not found."));
        }

        return token;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        await Context.RefreshTokens.AddAsync(token, cancellationToken);

        return await SaveChangesAsync(ApiErrorCodes.RefreshToken.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> RotateAsync(
        RefreshToken current,
        RefreshToken replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);

        // Домен уже проставил отзыв (RefreshToken.Revoke): из этих значений строится UPDATE.
        // Без них ротация неопределена — это ошибка вызывающего кода, а не состояние строки.
        if (current.RevokedAt is not { } revokedAt || current.ReplacedBy is not { } replacedBy)
        {
            return UnitResult.Failure(new Failure(Error.Validation(
                ApiErrorCodes.RefreshToken.RotateNotRevoked,
                "The current token must be marked as revoked before it is rotated.")));
        }

        // Вставка замены обязана быть в одной транзакции с отзывом: иначе между ними
        // остаётся окно, в котором старый токен уже мёртв, а нового ещё нет.
        var ownsTransaction = Context.Database.CurrentTransaction is null;

        await using var transaction = ownsTransaction
            ? await Context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // 1. Claim. Условный UPDATE — единственная точка, где решается, кто выиграл
            //    ротацию. Два параллельных запроса проходят проверки IsReplaced/IsRevoked
            //    на своих снимках (оба видят revoked_at = NULL), но строку отзывает только
            //    первый: второй ждёт блокировку, а после COMMIT победителя предикат уже
            //    ложен, поэтому UPDATE возвращает 0 строк и замена не вставляется (R-07).
            //    replaced_by здесь ещё не пишется: self-FK проверяется сразу, а строки
            //    замены до шага 2 не существует.
            var rows = await Context.RefreshTokens
                .Where(x => x.Id == current.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.RevokedAt, revokedAt)
                        .SetProperty(x => x.UpdatedAt, current.UpdatedAt),
                    cancellationToken);

            // ExecuteUpdate идёт мимо change tracker: изменённый в памяти current остался бы
            // Modified, и следующий SaveChanges повторил бы UPDATE уже без условия.
            Context.Entry(current).State = EntityState.Detached;

            if (rows == 0)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return UnitResult.Failure(new Failure(Error.Conflict(
                    IRefreshTokenRepository.AlreadyRotatedErrorCode,
                    "The refresh token was already rotated by a concurrent request.")));
            }

            // 2. Замена. Вставляется в той же транзакции: либо есть и отзыв, и замена,
            //    либо нет ничего.
            await Context.RefreshTokens.AddAsync(replacement, cancellationToken);

            var saved = await SaveChangesAsync(ApiErrorCodes.RefreshToken.RotatePrefix, cancellationToken);
            if (saved.IsFailure)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return saved;
            }

            // 3. Звено цепочки: replaced_by указывает на уже вставленную строку.
            await Context.RefreshTokens
                .Where(x => x.Id == current.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.ReplacedBy, replacedBy),
                    cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return UnitResult.Success<Failure>();
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    public Task<UnitResult<Failure>> RevokeAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        return SaveGraphAsync(token, ApiErrorCodes.RefreshToken.RevokePrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> RevokeChainAsync(
        Guid tokenId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        // Рекурсивный запрос идёт вперёд по replaced_by: revoke за один вызов гасит
        // и предъявленный (уже заменённый) токен, и всех его потомков.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             WITH RECURSIVE chain AS (
                 SELECT id, replaced_by
                 FROM refresh_tokens
                 WHERE id = {tokenId}
                 UNION ALL
                 SELECT token.id, token.replaced_by
                 FROM refresh_tokens AS token
                 INNER JOIN chain ON token.id = chain.replaced_by
             )
             UPDATE refresh_tokens
             SET revoked_at = {revokedAt},
                 updated_at = {revokedAt}
             WHERE id IN (SELECT id FROM chain)
               AND revoked_at IS NULL
             """,
            cancellationToken);

        // UPDATE прошёл мимо EF Core: строка в базе отозвана, а отслеживаемый экземпляр
        // (например, current из GetByHashAsync) всё ещё хранит RevokedAt = null. Если оставить
        // его в трекере, следующий SaveChanges этим экземпляром вернул бы revoked_at в NULL.
        // Состав цепочки в памяти неизвестен — его строит рекурсивный CTE, — а refresh-токены
        // читает только этот репозиторий, поэтому снимаем все отслеживаемые звенья.
        DetachTracked();

        return UnitResult.Success<Failure>();
    }

    public async Task<UnitResult<Failure>> RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        await Context.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedAt, revokedAt)
                    .SetProperty(x => x.UpdatedAt, revokedAt),
                cancellationToken);

        // Та же причина, что и в RevokeChainAsync: ExecuteUpdate обошёл change tracker, и без
        // очистки следующий SaveChanges записал бы отслеживаемым токенам пользователя
        // RevokedAt = null. Снимаем все его токены: после массового отзыва они не должны
        // участвовать в следующем SaveChanges, а вычислять затронутые по памяти ненадёжно.
        DetachTracked(token => token.UserId == userId);

        return UnitResult.Success<Failure>();
    }

    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        // ExecuteDelete идёт мимо change tracker: удалённые строки могли остаться
        // отслеживаемыми, и следующий SaveChanges попытался бы их обновить. Снимаем ровно
        // те, что попали под предикат: cutoff тот же, что у DELETE.
        var deleted = await Context.RefreshTokens
            .Where(x => x.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        DetachTracked(token => token.ExpiresAt < cutoff);

        return deleted;
    }
}
