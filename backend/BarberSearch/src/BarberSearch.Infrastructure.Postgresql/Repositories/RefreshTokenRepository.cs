using BarberSearch.Application.RefreshTokens;
using BarberSearch.Domain.RefreshTokens;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий refresh-токенов. Открытые токены сюда не попадают — только SHA-256.
/// </summary>
public sealed class RefreshTokenRepository : RepositoryBase<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<Result<RefreshToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return new Failure(Error.Validation(
                "refreshToken.getByHash.tokenHash",
                "Token hash cannot be empty.",
                nameof(tokenHash)));
        }

        var token = await Context.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            return new Failure(Error.NotFound(
                "refreshToken.getByHash.notFound",
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

        return await SaveChangesAsync("refreshToken.add", cancellationToken);
    }

    public async Task<UnitResult<Failure>> RotateAsync(
        RefreshToken current,
        RefreshToken replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);

        if (Context.Entry(current).State == EntityState.Detached)
        {
            Context.RefreshTokens.Update(current);
        }

        await Context.RefreshTokens.AddAsync(replacement, cancellationToken);

        return await SaveChangesAsync("refreshToken.rotate", cancellationToken);
    }

    public Task<UnitResult<Failure>> RevokeAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        return SaveGraphAsync(token, "refreshToken.revoke", cancellationToken);
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

        return UnitResult.Success<Failure>();
    }
}
