using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Emails;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailChanges;
using BarberSearch.Domain.EmailOutbox;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.EmailChanges.RequestEmailChange;

/// <summary>
/// Проверяет заявку и ставит в outbox два письма: ссылку на новый адрес и уведомление на старый.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок фиксирован: пользователь → нормализация и синтаксис → совпадение с текущим
/// адресом → <b>текущий пароль</b> → занятость адреса → доступность почты и <c>App:PublicUrl</c>
/// → кулдаун → одна транзакция «отозвать активный токен, вставить новый и положить оба письма
/// в outbox».
/// </para>
/// <para>
/// Пароль проверяется после дешёвых проверок, но до обращения к адресам: запрос, который
/// раскрывает существование чужого адреса (<c>409 emailTaken</c>), не должен отвечать без
/// подтверждения личности. PBKDF2-сверка — самая дорогая часть запроса.
/// </para>
/// <para>
/// Ни один адрес и ни один открытый токен в лог не попадают: только идентификатор
/// пользователя и коды ошибок.
/// </para>
/// </remarks>
public sealed class RequestEmailChangeHandler
    : ICommandHandler<RequestEmailChangeCommand>
{
    private const string INVALID_PASSWORD_MESSAGE = "The current password is incorrect.";

    private const string EMAIL_TAKEN_MESSAGE =
        "This email address is already in use by another account.";

    private const string UNAVAILABLE_MESSAGE =
        "Email change is not configured on this server.";

    private const string COOLDOWN_MESSAGE =
        "The email change has already been requested. Try again in a minute.";

    private readonly IUserRepository _users;
    private readonly IEmailChangeTokenRepository _tokens;
    private readonly IEmailOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordService _passwords;
    private readonly IEmailSenderAvailability _emailAvailability;
    private readonly AppOptions _appOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestEmailChangeHandler> _logger;

    public RequestEmailChangeHandler(
        IUserRepository users,
        IEmailChangeTokenRepository tokens,
        IEmailOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        IPasswordService passwords,
        IEmailSenderAvailability emailAvailability,
        AppOptions appOptions,
        TimeProvider timeProvider,
        ILogger<RequestEmailChangeHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(passwords);
        ArgumentNullException.ThrowIfNull(emailAvailability);
        ArgumentNullException.ThrowIfNull(appOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _users = users;
        _tokens = tokens;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _passwords = passwords;
        _emailAvailability = emailAvailability;
        _appOptions = appOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет заявку, выпускает токен и ставит письма в outbox.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RequestEmailChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userResult = await _users.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var normalized = EmailAddress.Normalize(command.NewEmail);

        var emailError = EmailAddress.Validate(
            normalized,
            ApiErrorCodes.User.ChangeEmailNewEmail,
            "newEmail");
        if (emailError is not null)
        {
            return new Failure(emailError);
        }

        // Сравнение с каноническим адресом: «user@x» и «User@x» — один и тот же адрес.
        if (string.Equals(user.Email, normalized, StringComparison.Ordinal))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.User.ChangeEmailSameEmail,
                "The new email is the same as the current one.",
                "newEmail"));
        }

        // Пароль — до проверки занятости: ответ «адрес занят» не должен доставаться тому,
        // кто не подтвердил личность. Хеш сравнивается как есть, политика к нему не применяется.
        if (_passwords.Verify(user.PasswordHash, command.CurrentPassword) ==
            PasswordVerification.Failed)
        {
            _logger.LogInformation(
                "Email change rejected for user {UserId}: the current password is incorrect.",
                user.Id);

            return new Failure(Error.Validation(
                ApiErrorCodes.Auth.EmailChangeInvalidCurrentPassword,
                INVALID_PASSWORD_MESSAGE,
                "currentPassword"));
        }

        var taken = await _users.EmailExistsAsync(normalized, cancellationToken);
        if (taken.IsFailure)
        {
            return taken.Error;
        }

        if (taken.Value)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.User.ChangeEmailEmailTaken,
                EMAIL_TAKEN_MESSAGE));
        }

        var publicUrl = _appOptions.PublicUrl?.TrimEnd('/');

        if (!_emailAvailability.IsAvailable || string.IsNullOrWhiteSpace(publicUrl))
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Auth.EmailChangeUnavailable,
                UNAVAILABLE_MESSAGE));
        }

        var now = _timeProvider.GetUtcNow();

        var activeResult = await _tokens.GetActiveByUserIdAsync(user.Id, cancellationToken);
        if (activeResult.IsFailure)
        {
            return activeResult.Error;
        }

        // Кулдаун считается по IssuedAt — бизнес-времени выдачи, а не по CreatedAt строки.
        // Ровно через 60 секунд повторная заявка разрешена: сравнение строгое.
        if (activeResult.Value is { } activeToken &&
            activeToken.IssuedAt > now - EmailChangeLifetime.RESEND_COOLDOWN)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Auth.EmailChangeCooldown,
                COOLDOWN_MESSAGE));
        }

        var rawToken = EmailChangeTokenGenerator.Generate();

        var created = EmailChangeToken.Create(
            user.Id,
            normalized,
            EmailChangeTokenGenerator.ComputeHash(rawToken),
            now,
            now + EmailChangeLifetime.TOKEN_LIFETIME,
            now);

        if (created.IsFailure)
        {
            return created.Error;
        }

        var token = created.Value;

        // Ссылка собирается ровно в одном месте; PublicUrl уже проверен на пустоту.
        var link =
            $"{publicUrl}/confirm-email-change?token={Uri.EscapeDataString(rawToken)}";

        var confirmation = EmailChangeConfirmationMessage.Create(
            normalized,
            user.FirstName,
            normalized,
            link,
            (int)EmailChangeLifetime.TOKEN_LIFETIME.TotalHours);

        var notice = EmailChangeNoticeMessage.Create(
            user.Email,
            user.FirstName,
            normalized);

        // Ключ дедупликации ссылки — id токена: одно письмо на одну выданную строку.
        // Уведомление получает свой ключ: это второе письмо той же операции, и без префикса
        // оно столкнулось бы с ключом ссылки.
        var queuedConfirmation = EmailOutboxMessage.Create(
            token.Id.ToString("D"),
            confirmation.To,
            confirmation.Subject,
            confirmation.HtmlBody,
            confirmation.TextBody,
            now);

        if (queuedConfirmation.IsFailure)
        {
            return queuedConfirmation.Error;
        }

        var queuedNotice = EmailOutboxMessage.Create(
            $"email-change-notice:{token.Id:D}",
            notice.To,
            notice.Subject,
            notice.HtmlBody,
            notice.TextBody,
            now);

        if (queuedNotice.IsFailure)
        {
            return queuedNotice.Error;
        }

        // Отзыв предыдущего активного, вставка нового токена и обе строки outbox — одна
        // транзакция: в базе не должно остаться двух активных строк, а письмо не должно
        // пережить откат токена (или наоборот). Повторное чтение внутри транзакции закрывает
        // окно между проверкой кулдауна и записью; частичный уникальный индекс — последняя
        // защита.
        var persisted = await _unitOfWork.ExecuteAsync(async transactionToken =>
        {
            var existingResult =
                await _tokens.GetActiveByUserIdAsync(user.Id, transactionToken);
            if (existingResult.IsFailure)
            {
                return existingResult.Error;
            }

            if (existingResult.Value is { } existing)
            {
                var revoke = existing.Revoke(now);
                if (revoke.IsFailure)
                {
                    return revoke.Error;
                }

                var updated = await _tokens.UpdateAsync(existing, transactionToken);
                if (updated.IsFailure)
                {
                    return updated.Error;
                }
            }

            var addedToken = await _tokens.AddAsync(token, transactionToken);
            if (addedToken.IsFailure)
            {
                return addedToken.Error;
            }

            var addedConfirmation =
                await _outbox.AddAsync(queuedConfirmation.Value, transactionToken);
            if (addedConfirmation.IsFailure)
            {
                return addedConfirmation.Error;
            }

            return await _outbox.AddAsync(queuedNotice.Value, transactionToken);
        }, cancellationToken);

        if (persisted.IsFailure)
        {
            return persisted.Error;
        }

        // Письма ещё не отправлены — их отправит диспетчер. В лог не попадают ни адреса,
        // ни токен: только пользователь и факт постановки в очередь.
        _logger.LogInformation(
            "Email change messages for user {UserId} have been queued in the outbox.",
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
