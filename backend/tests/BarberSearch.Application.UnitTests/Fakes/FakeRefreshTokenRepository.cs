using System.Reflection;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.RefreshTokens;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IRefreshTokenRepository"/>. Цепочка ротации хранится через
/// <see cref="RefreshToken.ReplacedBy"/>, поэтому повторное использование старого токена
/// можно проверить без базы данных.
/// </summary>
/// <remarks>
/// <para>
/// Чтение возвращает копию строки: два параллельных обработчика получают независимые
/// снимки, как два <c>DbContext</c> в PostgreSQL. Изменения копятся на копии и попадают
/// в «таблицу» только через <see cref="RotateAsync"/> / <see cref="RevokeAsync"/> /
/// <see cref="RevokeChainAsync"/> / <see cref="RevokeAllForUserAsync"/>.
/// </para>
/// <para>
/// Условный <c>UPDATE … WHERE revoked_at IS NULL</c> из
/// <c>RefreshTokenRepository.RotateAsync</c> моделируется здесь проверкой
/// <see cref="RefreshToken.IsRevoked"/> на сохранённой строке: побеждает первый вызов,
/// второй получает <see cref="IRefreshTokenRepository.AlreadyRotatedErrorCode"/>.
/// </para>
/// </remarks>
public sealed class FakeRefreshTokenRepository : IRefreshTokenRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, RefreshToken> _tokens = [];
    private Dictionary<Guid, RefreshToken>? _snapshot;
    private TaskCompletionSource? _readGate;
    private int _readArrivals;

    public IReadOnlyCollection<RefreshToken> Tokens => _tokens.Values;

    /// <summary>
    /// Подмена отзыва всех токенов пользователя: тест проверяет откат транзакции,
    /// когда второй шаг операции (смена пароля, роли, деактивация) падает.
    /// </summary>
    public Func<Guid, DateTimeOffset, UnitResult<Failure>>? RevokeAllBehavior { get; set; }

    public void BeginTransaction()
        => _snapshot = _tokens.ToDictionary(
            entry => entry.Key,
            entry => Clone(entry.Value));

    public void CommitTransaction() => _snapshot = null;

    public void RollbackTransaction()
    {
        if (_snapshot is null)
        {
            return;
        }

        _tokens.Clear();

        foreach (var entry in _snapshot)
        {
            _tokens[entry.Key] = entry.Value;
        }

        _snapshot = null;
    }

    /// <summary>
    /// Сколько параллельных чтений должны встретиться, прежде чем любое из них продолжит.
    /// Моделирует окно гонки: оба запроса делают <c>SELECT</c> раньше первого <c>UPDATE</c>.
    /// </summary>
    public int ReadRendezvous { get; set; }

    public RefreshToken? FindByRawToken(string rawToken)
    {
        var hash = RefreshTokenGenerator.ComputeHash(rawToken);

        return _tokens.Values.FirstOrDefault(x => x.TokenHash == hash);
    }

    public async Task<Result<RefreshToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        var token = _tokens.Values.FirstOrDefault(x => x.TokenHash == tokenHash);

        if (token is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.RefreshToken.NotFound,
                "Refresh token was not found."));
        }

        // Снимок снимается до барьера: оба параллельных запроса видят состояние строки
        // до первой ротации — ровно как два SELECT до первого UPDATE.
        var snapshot = Clone(token);

        await WaitForReadRendezvousAsync().ConfigureAwait(false);

        return snapshot;
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
        if (!_tokens.TryGetValue(current.Id, out var stored))
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.NotFound(
                ApiErrorCodes.RefreshToken.NotFound,
                "Refresh token was not found."))));
        }

        if (current.RevokedAt is not { } revokedAt || current.ReplacedBy is not { } replacedBy)
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Validation(
                ApiErrorCodes.RefreshToken.RotateNotRevoked,
                "The current token must be marked as revoked before it is rotated."))));
        }

        // Аналог WHERE revoked_at IS NULL: строку отзывает только первый из конкурентов.
        if (stored.IsRevoked)
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                IRefreshTokenRepository.AlreadyRotatedErrorCode,
                "The refresh token was already rotated by a concurrent request."))));
        }

        stored.Revoke(revokedAt, replacedBy);
        _tokens[replacement.Id] = replacement;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> RevokeAsync(
        RefreshToken token,
        CancellationToken cancellationToken = default)
    {
        // Обработчик мутирует свой снимок, «таблица» сохраняет изменение отдельно.
        if (_tokens.TryGetValue(token.Id, out var stored)
            && !stored.IsRevoked
            && token.RevokedAt is { } revokedAt)
        {
            stored.Revoke(revokedAt, token.ReplacedBy);
        }

        return Task.FromResult(UnitResult.Success<Failure>());
    }

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
        if (RevokeAllBehavior is { } behavior)
        {
            return Task.FromResult(behavior(userId, revokedAt));
        }

        foreach (var token in _tokens.Values.Where(x => x.UserId == userId && !x.IsRevoked))
        {
            token.Revoke(revokedAt);
        }

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<int> DeleteExpiredAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        // Граница та же, что у SQL-реализации: строго раньше cutoff. Цепочки не разбираются —
        // удаляется каждая истёкшая строка, как и одним DELETE в PostgreSQL.
        var expiredIds = _tokens.Values
            .Where(x => x.ExpiresAt < cutoff)
            .Select(x => x.Id)
            .ToList();

        foreach (var id in expiredIds)
        {
            _tokens.Remove(id);
        }

        return Task.FromResult(expiredIds.Count);
    }

    private async Task WaitForReadRendezvousAsync()
    {
        if (ReadRendezvous <= 1)
        {
            return;
        }

        _readGate ??= new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (Interlocked.Increment(ref _readArrivals) >= ReadRendezvous)
        {
            _readGate.TrySetResult();
        }

        await _readGate.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Копия строки с тем же <c>Id</c>: <see cref="RefreshToken"/> мутирует обработчик,
    /// а «таблица» должна остаться нетронутой до явной записи.
    /// </summary>
    private static RefreshToken Clone(RefreshToken token)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (RefreshToken)memberwiseClone.Invoke(token, null)!;
    }
}
