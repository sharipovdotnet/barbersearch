using System.Reflection;
using BarberSearch.Application.PasswordResets;
using BarberSearch.Domain.PasswordResets;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IPasswordResetTokenRepository"/>.
/// </summary>
/// <remarks>
/// Чтение возвращает копию строки: два параллельных обработчика получают независимые
/// снимки, как два <c>DbContext</c> в PostgreSQL. Изменения копятся на копии и попадают
/// в «таблицу» только через <see cref="AddAsync"/> / <see cref="UpdateAsync"/>.
/// <para>
/// Частичный уникальный индекс активных строк моделируется в <see cref="AddAsync"/>: вторая
/// неиспользованная и неотозванная строка пользователя даёт внутренний
/// <c>409 passwordResetToken.issue.conflict</c> — ровно как настоящий репозиторий. Этот код
/// не публичный: обработчик <c>request</c> обязан погасить его в тихий успех.
/// </para>
/// </remarks>
public sealed class FakePasswordResetTokenRepository
    : IPasswordResetTokenRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, PasswordResetToken> _tokens = [];
    private Dictionary<Guid, PasswordResetToken>? _snapshot;

    public IReadOnlyCollection<PasswordResetToken> Tokens => _tokens.Values;

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

    public int DeleteCallCount { get; private set; }

    /// <summary>
    /// Аргумент <c>now</c> последнего вызова <see cref="DeleteExpiredAsync"/>.
    /// </summary>
    public DateTimeOffset? LastDeleteNow { get; private set; }

    /// <summary>
    /// Аргумент <c>retention</c> последнего вызова <see cref="DeleteExpiredAsync"/>.
    /// </summary>
    public TimeSpan? LastDeleteRetention { get; private set; }

    /// <summary>
    /// Подмена чтения по хешу: тест проверяет, что не-«not found» ошибка репозитория
    /// не превращается в <c>400</c>.
    /// </summary>
    public Func<string, Result<PasswordResetToken, Failure>>? GetByHashBehavior { get; set; }

    /// <summary>
    /// Подмена вставки: тест проверяет гонку за активный индекс, сбой транзакции и откат отзыва.
    /// </summary>
    public Func<PasswordResetToken, UnitResult<Failure>>? AddBehavior { get; set; }

    /// <summary>
    /// Подмена сохранения: тест проверяет проигранную гонку оптимистической блокировки.
    /// </summary>
    public Func<PasswordResetToken, UnitResult<Failure>>? UpdateBehavior { get; set; }

    /// <summary>
    /// Исключение, которое бросит <see cref="DeleteExpiredAsync"/>: уборка должна залогировать
    /// сбой и не выбросить его наружу.
    /// </summary>
    public Exception? DeleteException { get; set; }

    /// <summary>
    /// Наблюдатель вставки: тест может вклиниться между чтением и записью.
    /// </summary>
    public Action<PasswordResetToken>? OnAdd { get; set; }

    /// <summary>
    /// Наблюдатель обновления.
    /// </summary>
    public Action<PasswordResetToken>? OnUpdate { get; set; }

    public void AddRaw(PasswordResetToken token) => _tokens[token.Id] = token;

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

    public Task<Result<PasswordResetToken, Failure>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        GetByHashCallCount++;

        if (GetByHashBehavior is { } behavior)
        {
            return Task.FromResult(behavior(tokenHash));
        }

        var token = _tokens.Values.FirstOrDefault(x => x.TokenHash == tokenHash);

        if (token is null)
        {
            return Task.FromResult<Result<PasswordResetToken, Failure>>(
                new Failure(Error.NotFound(
                    "passwordResetToken.notFound",
                    "Password reset token was not found.")));
        }

        return Task.FromResult<Result<PasswordResetToken, Failure>>(Clone(token));
    }

    public Task<Result<PasswordResetToken?, Failure>> GetActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        GetActiveCallCount++;

        var token = _tokens.Values
            .Where(x => x.UserId == userId && x.ConsumedAt is null && x.RevokedAt is null)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefault();

        return Task.FromResult<Result<PasswordResetToken?, Failure>>(
            token is null ? null : Clone(token));
    }

    public Task<UnitResult<Failure>> AddAsync(
        PasswordResetToken token,
        CancellationToken cancellationToken = default)
    {
        AddCallCount++;
        AddInTransaction |= IsInTransaction?.Invoke() ?? false;
        OnAdd?.Invoke(token);

        if (AddBehavior is { } behavior)
        {
            return Task.FromResult(behavior(token));
        }

        // Аналог ux_password_reset_tokens_active: вторая активная строка пользователя
        // невозможна. Проигравший получает внутренний код, а не публичную ошибку.
        if (_tokens.Values.Any(x =>
                x.UserId == token.UserId
                && x.ConsumedAt is null
                && x.RevokedAt is null))
        {
            return Task.FromResult(UnitResult.Failure(new Failure(Error.Conflict(
                "passwordResetToken.issue.conflict",
                "Another active password reset token was issued concurrently."))));
        }

        _tokens[token.Id] = Clone(token);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        PasswordResetToken token,
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
        DeleteCallCount++;
        LastDeleteNow = now;
        LastDeleteRetention = retention;

        if (DeleteException is { } exception)
        {
            throw exception;
        }

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
    private static PasswordResetToken Clone(PasswordResetToken token)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (PasswordResetToken)memberwiseClone.Invoke(token, null)!;
    }
}
