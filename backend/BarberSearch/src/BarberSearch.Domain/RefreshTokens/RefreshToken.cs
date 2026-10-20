using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.RefreshTokens;

/// <summary>
/// Refresh-токен: длинная половина пары access/refresh. Открытый токен в базе не хранится —
/// только SHA-256, поэтому утечка таблицы не даёт готовых учётных данных.
/// </summary>
/// <remarks>
/// Токен живёт 7–30 дней. Ротация помечает старую строку <see cref="RevokedAt"/> и
/// <see cref="ReplacedBy"/>; повторное предъявление заменённого токена — признак кражи,
/// и приложение отзывает всю цепочку.
/// </remarks>
public class RefreshToken : AggregateRoot
{
    /// <summary>
    /// Минимальный срок жизни refresh-токена в днях.
    /// </summary>
    public const int MIN_LIFETIME_DAYS = 7;

    /// <summary>
    /// Максимальный срок жизни refresh-токена в днях.
    /// </summary>
    public const int MAX_LIFETIME_DAYS = 30;

    /// <summary>
    /// Длина SHA-256 в шестнадцатеричной записи.
    /// </summary>
    public const int HASH_LENGTH = 64;

    /// <summary>
    /// Владелец токена. Навигации на <c>User</c> нет — это граница другого агрегата.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// SHA-256 от открытого токена, нижний регистр. Уникален.
    /// </summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>
    /// Момент истечения срока жизни.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Момент отзыва: <c>null</c>, пока токен жив.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// Токен, которым эта строка заменена при ротации. Вместе с <see cref="RevokedAt"/>
    /// образует цепочку, по которой ищется актуальный потомок.
    /// </summary>
    public Guid? ReplacedBy { get; private set; }

    /// <summary>
    /// Токен отозван (logout, смена роли, смена пароля, деактивация или ротация).
    /// </summary>
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>
    /// Токен был заменён при ротации. Повторное использование такого токена — сигнал кражи.
    /// </summary>
    public bool IsReplaced => RevokedAt is not null && ReplacedBy is not null;

    private RefreshToken()
    {
    }

    /// <summary>
    /// Создаёт строку refresh-токена. Открытый токен сюда уже не попадает — только его хеш.
    /// </summary>
    public static Result<RefreshToken, Failure> Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        var errors = DomainErrors.Collect();

        if (userId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                "refreshToken.create.userId",
                "User identifier cannot be empty.",
                nameof(userId)));
        }

        if (string.IsNullOrWhiteSpace(tokenHash) ||
            tokenHash.Length != HASH_LENGTH ||
            !tokenHash.All(char.IsAsciiHexDigit))
        {
            errors.Add(Error.Validation(
                "refreshToken.create.tokenHash",
                $"Token hash must be a {HASH_LENGTH}-character hexadecimal string.",
                nameof(tokenHash)));
        }

        if (expiresAt <= now)
        {
            errors.Add(Error.Validation(
                "refreshToken.create.expiresAt",
                "Expiration time must be in the future.",
                nameof(expiresAt)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Токен ещё можно использовать: он не отозван и не истёк.
    /// </summary>
    public bool IsActive(DateTimeOffset now) => !IsRevoked && ExpiresAt > now;

    /// <summary>
    /// Отзывает токен. При ротации передаётся <paramref name="replacedBy"/>; повторный отзыв
    /// идемпотентен — logout и фоновая уборка не должны падать друг из-за друга.
    /// </summary>
    public UnitResult<Failure> Revoke(DateTimeOffset now, Guid? replacedBy = null)
    {
        if (replacedBy == Guid.Empty)
        {
            return new Failure(Error.Validation(
                "refreshToken.revoke.replacedBy",
                "Replacement identifier cannot be empty.",
                nameof(replacedBy)));
        }

        if (IsRevoked)
        {
            return DomainErrors.Ok();
        }

        RevokedAt = now;
        ReplacedBy = replacedBy;
        MarkUpdated();

        return DomainErrors.Ok();
    }
}
