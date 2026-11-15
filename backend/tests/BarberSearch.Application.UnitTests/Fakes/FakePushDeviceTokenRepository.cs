using BarberSearch.Application.Push;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IPushDeviceTokenRepository"/> для тестов обработчиков.
/// </summary>
/// <remarks>
/// Фейк повторяет поведение PostgreSQL-репозитория: upsert по токену, передачу владения
/// и удаление самых старых устройств сверх <c>maxTokensPerUser</c>. Поэтому тест предела
/// устройств проверяет правило, а не отсутствие проверки.
/// </remarks>
public sealed class FakePushDeviceTokenRepository : IPushDeviceTokenRepository
{
    private readonly List<PushDeviceToken> _tokens = [];

    public IReadOnlyList<PushDeviceToken> Items => _tokens;

    public int RegisterCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public DateTimeOffset? LastDeleteStaleCutoff { get; private set; }

    /// <summary>
    /// Подмена поведения регистрации — для проверки ошибки хранилища.
    /// </summary>
    public Func<PushDeviceToken, UnitResult<Failure>>? OnRegister { get; set; }

    public void Seed(PushDeviceToken token) => _tokens.Add(token);

    public Task<UnitResult<Failure>> RegisterAsync(
        PushDeviceToken token,
        int maxTokensPerUser,
        CancellationToken cancellationToken = default)
    {
        RegisterCalls++;

        if (OnRegister is not null)
        {
            return Task.FromResult(OnRegister(token));
        }

        var existing = _tokens.FirstOrDefault(candidate => candidate.Token == token.Token);

        if (existing is null)
        {
            _tokens.Add(token);
        }
        else
        {
            // Повторяем GREATEST из upsert: часы с разных инстансов не могут откатить
            // last_seen_at назад и вытеснить самое свежее устройство.
            var seenAt = token.LastSeenAt > existing.LastSeenAt
                ? token.LastSeenAt
                : existing.LastSeenAt;

            existing.TransferTo(token.UserId, seenAt);
        }

        // Предел: оставляем самые свежие, остальные (самые давно не заходившие) удаляем.
        var limit = maxTokensPerUser > 0
            ? maxTokensPerUser
            : PushOptions.DEFAULT_MAX_TOKENS_PER_USER;

        var fresh = _tokens
            .Where(candidate => candidate.UserId == token.UserId)
            .OrderByDescending(candidate => candidate.LastSeenAt)
            .ThenByDescending(candidate => candidate.Id)
            .ToList();

        foreach (var stale in fresh.Skip(limit))
        {
            _tokens.Remove(stale);
        }

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<Result<IReadOnlyList<PushDeviceToken>, Failure>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PushDeviceToken> rows = _tokens
            .Where(token => token.UserId == userId)
            .OrderByDescending(token => token.LastSeenAt)
            .ThenByDescending(token => token.Id)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<PushDeviceToken>, Failure>(rows));
    }

    public Task<Result<int, Failure>> DeleteAsync(
        string token,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls++;

        var removed = _tokens.RemoveAll(candidate =>
            candidate.Token == token && candidate.UserId == userId);

        return Task.FromResult(Result.Success<int, Failure>(removed));
    }

    public Task<Result<int, Failure>> DeleteByTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls++;

        var removed = _tokens.RemoveAll(candidate => candidate.Token == token);

        return Task.FromResult(Result.Success<int, Failure>(removed));
    }

    public Task<Result<int, Failure>> DeleteStaleAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        LastDeleteStaleCutoff = cutoff;

        var removed = _tokens.RemoveAll(candidate => candidate.LastSeenAt < cutoff);

        return Task.FromResult(Result.Success<int, Failure>(removed));
    }
}
