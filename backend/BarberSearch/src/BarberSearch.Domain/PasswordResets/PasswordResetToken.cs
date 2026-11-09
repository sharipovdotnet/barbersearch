using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.PasswordResets;

/// <summary>
/// Одноразовый токен сброса пароля.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный агрегат, а не ребёнок <c>User</c> (как и <c>RefreshToken</c> /
/// <c>EmailVerificationToken</c>): таблица живёт своей жизнью, а связь с пользователем —
/// только <see cref="UserId"/>.
/// </para>
/// <para>
/// Открытый токен в базе не хранится: в <see cref="TokenHash"/> лежит SHA-256 в нижнем
/// регистре. Токен одноразовый: успешный сброс ставит <see cref="ConsumedAt"/>,
/// переотправка письма отзывает предыдущую активную строку через <see cref="RevokedAt"/>.
/// </para>
/// <para>
/// Домен не читает часы: и срок, и момент операции приходят аргументом. TTL (30 минут),
/// кулдаун переотправки (60 секунд) и ретенция уборки (30 дней) — решение Application;
/// домен проверяет только согласованность переданного времени.
/// </para>
/// </remarks>
public class PasswordResetToken : AggregateRoot
{
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
    /// Момент выдачи по бизнес-часам <c>TimeProvider</c>. По нему считается кулдаун
    /// переотправки, поэтому он не зависит от системных часов домена.
    /// </summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>
    /// Момент истечения срока жизни.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Момент успешного сброса пароля; <c>null</c>, пока токен не использован.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>
    /// Момент отзыва: переотправка письма гасит предыдущий активный токен.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    private PasswordResetToken()
    {
    }

    /// <summary>
    /// Создаёт строку токена. Открытый токен сюда уже не попадает — только его хеш.
    /// </summary>
    /// <param name="userId">Владелец токена; не пустой.</param>
    /// <param name="tokenHash">SHA-256 в нижнем регистре; ровно 64 hex-символа.</param>
    /// <param name="issuedAt">Момент выдачи по бизнес-часам; не <c>default</c> и не позже «сейчас».</param>
    /// <param name="expiresAt">Момент истечения; строго позже <paramref name="issuedAt"/>.</param>
    /// <param name="now">Текущий момент по бизнес-часам; не <c>default</c>.</param>
    public static Result<PasswordResetToken, Failure> Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        var errors = DomainErrors.Collect();

        if (userId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                "passwordResetToken.create.userId",
                "User identifier cannot be empty.",
                nameof(userId)));
        }

        if (string.IsNullOrWhiteSpace(tokenHash) ||
            tokenHash.Length != HASH_LENGTH ||
            !tokenHash.All(char.IsAsciiHexDigitLower))
        {
            errors.Add(Error.Validation(
                "passwordResetToken.create.tokenHash",
                $"Token hash must be a {HASH_LENGTH}-character lower-case hexadecimal string.",
                nameof(tokenHash)));
        }

        if (issuedAt == default)
        {
            errors.Add(Error.Validation(
                "passwordResetToken.create.issuedAt",
                "Issue time cannot be default.",
                nameof(issuedAt)));
        }
        else if (issuedAt > now)
        {
            errors.Add(Error.Validation(
                "passwordResetToken.create.issuedAt",
                "Issue time cannot be in the future.",
                nameof(issuedAt)));
        }

        if (now == default)
        {
            errors.Add(Error.Validation(
                "passwordResetToken.create.now",
                "Current time cannot be default.",
                nameof(now)));
        }

        if (expiresAt <= issuedAt)
        {
            errors.Add(Error.Validation(
                "passwordResetToken.create.expiresAt",
                "Expiration time must be later than the issue time.",
                nameof(expiresAt)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Токен ещё можно использовать: он не использован, не отозван и не истёк.
    /// </summary>
    /// <remarks>
    /// Сравнение строгое: в момент <see cref="ExpiresAt"/> токен уже недействителен.
    /// </remarks>
    public bool IsActive(DateTimeOffset now)
        => ConsumedAt is null && RevokedAt is null && now < ExpiresAt;

    /// <summary>
    /// Фиксирует успешное использование токена для сброса пароля.
    /// </summary>
    /// <remarks>
    /// Уже использованный токен — успех: повторный <c>Consume</c> идемпотентен на уровне
    /// домена. Прикладной слой одноразовость обеспечивает сам: уже потреблённый токен для
    /// confirm считается недействительным (§3.7). Отозванный токен — ошибка
    /// <c>passwordResetToken.consume.invalid</c>, истёкший — <c>passwordResetToken.consume.expired</c>.
    /// Отозванность проверяется раньше срока: старый отозванный токен не должен «оживать»
    /// по коду ошибки.
    /// </remarks>
    public UnitResult<Failure> Consume(DateTimeOffset now)
    {
        if (ConsumedAt is not null)
        {
            return DomainErrors.Ok();
        }

        if (RevokedAt is not null)
        {
            return new Failure(Error.Validation(
                "passwordResetToken.consume.invalid",
                "The password reset token has been revoked."));
        }

        if (now >= ExpiresAt)
        {
            return new Failure(Error.Validation(
                "passwordResetToken.consume.expired",
                "The password reset token has expired."));
        }

        ConsumedAt = now;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отзывает токен: переотправка письма гасит предыдущий активный.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: уже использованный или отозванный токен — успех. Активный, в том числе
    /// просроченный, получает <see cref="RevokedAt"/> — переотправке не нужно разбираться
    /// со сроками.
    /// </remarks>
    public UnitResult<Failure> Revoke(DateTimeOffset now)
    {
        if (ConsumedAt is not null || RevokedAt is not null)
        {
            return DomainErrors.Ok();
        }

        RevokedAt = now;
        MarkUpdated();

        return DomainErrors.Ok();
    }
}
