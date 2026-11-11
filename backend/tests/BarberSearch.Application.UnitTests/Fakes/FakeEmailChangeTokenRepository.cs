using System.Reflection;
using BarberSearch.Application.EmailChanges;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailChanges;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IEmailChangeTokenRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Чтение возвращает копию строки: два параллельных обработчика получают независимые
/// снимки, как два <c>DbContext</c> в PostgreSQL. Изменения копятся на копии и попадают
/// в «таблицу» только через <see cref="AddAsync"/> / <see cref="UpdateAsync"/>.
/// </para>
/// <para>
/// Частичный уникальный индекс моделируется в <see cref="AddAsync"/>: вторая неиспользованная
/// и неотозванная строка пользователя даёт <c>409 auth.emailChange.cooldown</c> — тот же
/// публичный код, что и у настоящего репозитория.
/// </para>
/// </remarks>
public sealed class FakeEmailChangeTokenRepository
    : IEmailChangeTokenRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, EmailChangeToken> _tokens = [];
    private Dictionary<Guid, EmailChangeToken>? _snapshot;

    /// <summary>
    /// Строки «таблицы» в порядке вставки.
    /// </summary>
    public IReadOnlyCollection<EmailChangeToken> Tokens => _tokens.Values;

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
    public Func<EmailChangeToken, UnitResult<Failure>>? AddBehavior { get; set; }

    /// <summary>
    /// Подмена обновления: тест проверяет сбой записи токена при подтверждении.
    /// </summary>
    public Func<EmailChangeToken, UnitResult<Failure>>? UpdateBehavior { get; set; }

    /// <summary>
    /// Наблюдатель вставки: тест может вклиниться между чтением и записью.
    /// </summary>
    public Action<EmailChangeToken>? OnAdd { get; set; }

    /// <summary>
    /// Наблюдатель обновления.
    /// </summary>
    public Action<EmailChangeToken>? OnUpdate { get; set; }

    public void AddRaw(EmailChangeToken token) => _tokens[token.Id] = token;

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

    public Task<Result<EmailChangeToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        GetByHashCallCount++;

        var token = _tokens.Values.FirstOrDefault(x => x.TokenHash == tokenHash);

        if (token is null)
        {
            return Task.FromResult<Result<EmailChangeToken, Failure>>(
                new Failure(Error.NotFound(
                    "emailChangeToken.notFound",
                    "Email change token was not found.")));
        }

        return Task.FromResult<Result<EmailChangeToken, Failure>>(Clone(token));
    }

    public Task<Result<EmailChangeToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        GetActiveCallCount++;

        var token = _tokens.Values
            .Where(x => x.UserId == userId && x.ConsumedAt is null && x.RevokedAt is null)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefault();

        return Task.FromResult<Result<EmailChangeToken?, Failure>>(
            token is null ? null : Clone(token));
    }

    public Task<UnitResult<Failure>> AddAsync(
        EmailChangeToken token,
        CancellationToken cancellationToken = default)
    {
        AddCallCount++;
        AddInTransaction |= IsInTransaction?.Invoke() ?? false;
        OnAdd?.Invoke(token);

        if (AddBehavior is { } behavior)
        {
            return Task.FromResult(behavior(token));
        }

        // Аналог ux_email_change_tokens_active: вторая активная строка пользователя
        // невозможна. Проигравший получает тот же публичный код, что и настоящий репозиторий.
        if (_tokens.Values.Any(x =>
                x.UserId == token.UserId
                && x.ConsumedAt is null
                && x.RevokedAt is null))
        {
            return Task.FromResult(UnitResult.Failure(new Failure(Error.Conflict(
                ApiErrorCodes.Auth.EmailChangeCooldown,
                "Another active email change token was issued concurrently."))));
        }

        _tokens[token.Id] = Clone(token);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        EmailChangeToken token,
        CancellationToken cancellationToken = default)
    {
        UpdateCallCount++;
        UpdateInTransaction |= IsInTransaction?.Invoke() ?? false;
        OnUpdate?.Invoke(token);

        if (UpdateBehavior is { } behavior)
        {
            return Task.FromResult(behavior(token));
        }

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
    private static EmailChangeToken Clone(EmailChangeToken token)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (EmailChangeToken)memberwiseClone.Invoke(token, null)!;
    }
}
