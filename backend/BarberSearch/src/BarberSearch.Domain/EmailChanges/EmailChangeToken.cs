using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.EmailChanges;

/// <summary>
/// Одноразовый токен смены адреса электронной почты: держит <b>новый</b> адрес до подтверждения.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный агрегат, а не ребёнок <c>User</c> (как <c>EmailVerificationToken</c>): таблица
/// живёт своей жизнью, а связь с пользователем — только <see cref="UserId"/>. Сам текущий
/// адрес не меняется до перехода по ссылке: в строке лежит только запрошенный новый.
/// </para>
/// <para>
/// Открытый токен в базе не хранится: в <see cref="TokenHash"/> лежит SHA-256 в нижнем
/// регистре. Токен одноразовый: успешное подтверждение ставит <see cref="ConsumedAt"/>,
/// повторная заявка отзывает предыдущую активную строку через <see cref="RevokedAt"/>.
/// </para>
/// <para>
/// Домен не читает часы: и срок, и момент операции приходят аргументом (TTL 24 часа и
/// кулдаун переотправки 60 секунд — решение Application).
/// </para>
/// </remarks>
public class EmailChangeToken : AggregateRoot
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
    /// Запрошенный адрес в каноническом виде: нормализован, уникальный индекс проверяется
    /// в момент подтверждения, а не здесь.
    /// </summary>
    public string NewEmail { get; private set; } = null!;

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
    /// Момент успешного подтверждения; <c>null</c>, пока токен не использован.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>
    /// Момент отзыва: новая заявка или сброс гасят предыдущий активный токен.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    private EmailChangeToken()
    {
    }

    /// <summary>
    /// Создаёт строку токена. Открытый токен сюда уже не попадает — только его хеш.
    /// </summary>
    /// <param name="userId">Владелец токена; не пустой.</param>
    /// <param name="newEmail">Запрошенный адрес; нормализуется и проверяется здесь.</param>
    /// <param name="tokenHash">SHA-256 в нижнем регистре; ровно 64 hex-символа.</param>
    /// <param name="issuedAt">Момент выдачи по бизнес-часам; не <c>default</c> и не позже «сейчас».</param>
    /// <param name="expiresAt">Момент истечения; строго позже <paramref name="issuedAt"/>.</param>
    /// <param name="now">Текущий момент по бизнес-часам; не <c>default</c>.</param>
    public static Result<EmailChangeToken, Failure> Create(
        Guid userId,
        string newEmail,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        var errors = DomainErrors.Collect();

        var normalizedEmail = EmailAddress.Normalize(newEmail);
        var emailError = EmailAddress.Validate(
            normalizedEmail,
            "emailChangeToken.create.newEmail",
            nameof(newEmail));
        if (emailError is not null)
        {
            errors.Add(emailError);
        }

        if (userId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                "emailChangeToken.create.userId",
                "User identifier cannot be empty.",
                nameof(userId)));
        }

        if (string.IsNullOrWhiteSpace(tokenHash) ||
            tokenHash.Length != HASH_LENGTH ||
            !tokenHash.All(char.IsAsciiHexDigitLower))
        {
            errors.Add(Error.Validation(
                "emailChangeToken.create.tokenHash",
                $"Token hash must be a {HASH_LENGTH}-character lower-case hexadecimal string.",
                nameof(tokenHash)));
        }

        if (issuedAt == default)
        {
            errors.Add(Error.Validation(
                "emailChangeToken.create.issuedAt",
                "Issue time cannot be default.",
                nameof(issuedAt)));
        }
        else if (issuedAt > now)
        {
            errors.Add(Error.Validation(
                "emailChangeToken.create.issuedAt",
                "Issue time cannot be in the future.",
                nameof(issuedAt)));
        }

        if (now == default)
        {
            errors.Add(Error.Validation(
                "emailChangeToken.create.now",
                "Current time cannot be default.",
                nameof(now)));
        }

        if (expiresAt <= issuedAt)
        {
            errors.Add(Error.Validation(
                "emailChangeToken.create.expiresAt",
                "Expiration time must be later than the issue time.",
                nameof(expiresAt)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new EmailChangeToken
        {
            UserId = userId,
            NewEmail = normalizedEmail,
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
    /// Фиксирует смену адреса.
    /// </summary>
    /// <remarks>
    /// Уже использованный токен — успех: повторный клик по ссылке идемпотентен.
    /// Отозванный токен — ошибка <c>emailChangeToken.consume.invalid</c>, истёкший —
    /// <c>emailChangeToken.consume.expired</c>. Отозванность проверяется раньше срока.
    /// </remarks>
    /// <param name="now">Текущий момент по бизнес-часам.</param>
    public UnitResult<Failure> Consume(DateTimeOffset now)
    {
        if (ConsumedAt is not null)
        {
            return DomainErrors.Ok();
        }

        if (RevokedAt is not null)
        {
            return new Failure(Error.Validation(
                "emailChangeToken.consume.invalid",
                "The email change token has been revoked."));
        }

        if (now >= ExpiresAt)
        {
            return new Failure(Error.Validation(
                "emailChangeToken.consume.expired",
                "The email change token has expired."));
        }

        ConsumedAt = now;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отзывает токен: новая заявка гасит предыдущий активный.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: уже использованный или отозванный токен — успех. Активный, в том числе
    /// просроченный, получает <see cref="RevokedAt"/> — новой заявке не нужно разбираться
    /// со сроками.
    /// </remarks>
    /// <param name="now">Текущий момент по бизнес-часам.</param>
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
