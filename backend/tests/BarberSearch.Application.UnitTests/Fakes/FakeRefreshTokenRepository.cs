using BarberSearch.Application.RefreshTokens;
using BarberSearch.Domain.RefreshTokens;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IRefreshTokenRepository"/>. Цепочка ротации хранится через
/// <see cref="RefreshToken.ReplacedBy"/>, поэтому повторное использование старого токена
/// можно проверить без базы данных.
/// </summary>
public sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly Dictionary<Guid, RefreshToken> _tokens = [];

    public IReadOnlyCollection<RefreshToken> Tokens => _tokens.Values;

    public RefreshToken? FindByRawToken(string rawToken)
    {
        var hash = RefreshTokenGenerator.ComputeHash(rawToken);

        return _tokens.Values.FirstOrDefault(x => x.TokenHash == hash);
    }

    public Task<Result<RefreshToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        var token = _tokens.Values.FirstOrDefault(x => x.TokenHash == tokenHash);

        if (token is null)
        {
            return Task.FromResult<Result<RefreshToken, Failure>>(new Failure(Error.NotFound(
                "refreshToken.getByHash.notFound",
                "Refresh token was not found.")));
        }

        return Task.FromResult<Result<RefreshToken, Failure>>(token);
    }

    public Task<UnitResult<Failure>> AddAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default)
    {
        _tokens[token.Id] = token;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> RotateAsync(
        RefreshToken current,
        RefreshToken replacement,
        CancellationToken cancellationToken = default)
    {
        _tokens[replacement.Id] = replacement;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> RevokeAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default)
        => Task.FromResult(UnitResult.Success<Failure>());

    public Task<UnitResult<Failure>> RevokeChainAsync(
        Guid tokenId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        var current = _tokens.GetValueOrDefault(tokenId);
        var visited = new HashSet<Guid>();

        while (current is not null && visited.Add(current.Id))
        {
            if (!current.IsRevoked)
            {
                current.Revoke(revokedAt);
            }

            current = current.ReplacedBy is { } next
                ? _tokens.GetValueOrDefault(next)
                : null;
        }

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        foreach (var token in _tokens.Values.Where(x => x.UserId == userId && !x.IsRevoked))
        {
            token.Revoke(revokedAt);
        }

        return Task.FromResult(UnitResult.Success<Failure>());
    }
}
