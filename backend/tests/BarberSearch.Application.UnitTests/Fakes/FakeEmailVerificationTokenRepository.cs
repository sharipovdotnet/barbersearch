using System.Reflection;
using BarberSearch.Application.EmailVerifications;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailVerifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IEmailVerificationTokenRepository"/>.
/// </summary>
/// <remarks>
/// Чтение возвращает копию строки: два параллельных обработчика получают независимые
/// снимки, как два <c>DbContext</c> в PostgreSQL. Изменения копятся на копии и попадают
/// в «таблицу» только через <see cref="AddAsync"/> / <see cref="UpdateAsync"/>.
/// <para>
/// Частичный уникальный индекс моделируется в <see cref="AddAsync"/>: вторая неиспользованная
/// и неотозванная строка пользователя даёт <c>409 auth.emailVerification.cooldown</c>.
/// </para>
/// </remarks>
public sealed class FakeEmailVerificationTokenRepository
    : IEmailVerificationTokenRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, EmailVerificationToken> _tokens = [];
    private Dictionary<Guid, EmailVerificationToken>? _snapshot;

    public IReadOnlyCollection<EmailVerificationToken> Tokens => _tokens.Values;

    /// <summary>
    /// Проверка «мы внутри транзакции»; тест подключает сюда <see cref="FakeUnitOfWork"/>.
    /// </summary>
    public Func<bool>? IsInTransaction { get; set; }

    /// <summary>
    /// Был ли <see cref="AddAsync"/> вызван внутри транзакции.
    /// </summary>
    public bool AddInTransaction { get; private set; }

    /// <summary>
    /// Был ли <see cref="UpdateAsync"/> вызван внутри транзакции.
    /// </summary>
    public bool UpdateInTransaction { get; private set; }

    public int GetByHashCallCount { get; private set; }

    public int GetActiveCallCount { get; private set; }

    public int AddCallCount { get; private set; }

    public int UpdateCallCount { get; private set; }

    /// <summary>
    /// Подмена вставки: тест проверяет откат отзыва при сбое второго шага транзакции.
    /// </summary>
    public Func<EmailVerificationToken, UnitResult<Failure>>? AddBehavior { get; set; }

    /// <summary>
    /// Наблюдатель вставки: тест может вклиниться между чтением и записью.
    /// </summary>
    public Action<EmailVerificationToken>? OnAdd { get; set; }

    /// <summary>
    /// Наблюдатель обновления.
    /// </summary>
    public Action<EmailVerificationToken>? OnUpdate { get; set; }

    public void AddRaw(EmailVerificationToken token) => _tokens[token.Id] = token;

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

    public Task<Result<EmailVerificationToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        GetByHashCallCount++;

        var token = _tokens.Values.FirstOrDefault(x => x.TokenHash == tokenHash);

        if (token is null)
        {
            return Task.FromResult<Result<EmailVerificationToken, Failure>>(
                new Failure(Error.NotFound(
                    "emailVerificationToken.notFound",
                    "Email verification token was not found.")));
        }

        return Task.FromResult<Result<EmailVerificationToken, Failure>>(Clone(token));
    }

    public Task<Result<EmailVerificationToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        GetActiveCallCount++;

        var token = _tokens.Values
            .Where(x => x.UserId == userId && x.ConsumedAt is null && x.RevokedAt is null)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefault();

        return Task.FromResult<Result<EmailVerificationToken?, Failure>>(
            token is null ? null : Clone(token));
    }

    public Task<UnitResult<Failure>> AddAsync(
        EmailVerificationToken token,
        CancellationToken cancellationToken = default)
    {
        AddCallCount++;
        AddInTransaction |= IsInTransaction?.Invoke() ?? false;
        OnAdd?.Invoke(token);

        if (AddBehavior is { } behavior)
        {
            return Task.FromResult(behavior(token));
        }

        // Аналог ux_email_verification_tokens_active: вторая активная строка пользователя
        // невозможна. Проигравший получает тот же публичный код, что и настоящий репозиторий.
        if (_tokens.Values.Any(x =>
                x.UserId == token.UserId
                && x.ConsumedAt is null
                && x.RevokedAt is null))
        {
            return Task.FromResult(UnitResult.Failure(new Failure(Error.Conflict(
                ApiErrorCodes.Auth.EmailVerificationCooldown,
                "Another active email verification token was issued concurrently."))));
        }

        _tokens[token.Id] = Clone(token);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        EmailVerificationToken token,
        CancellationToken cancellationToken = default)
    {
        UpdateCallCount++;
        UpdateInTransaction |= IsInTransaction?.Invoke() ?? false;
        OnUpdate?.Invoke(token);

        _tokens[token.Id] = Clone(token);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<int> DeleteExpiredAsync(
        DateTimeOffset now,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        var cutoff = now - retention;

        var staleIds = _tokens.Values
            .Where(x => x.ExpiresAt < cutoff
                        || (x.ConsumedAt is not null && x.ConsumedAt < cutoff))
            .Select(x => x.Id)
            .ToList();

        foreach (var id in staleIds)
        {
            _tokens.Remove(id);
        }

        return Task.FromResult(staleIds.Count);
    }

    /// <summary>
    /// Копия строки с тем же <c>Id</c>: обработчик мутирует свой снимок, а «таблица»
    /// должна остаться нетронутой до явной записи.
    /// </summary>
    private static EmailVerificationToken Clone(EmailVerificationToken token)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (EmailVerificationToken)memberwiseClone.Invoke(token, null)!;
    }
}
