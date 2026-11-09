using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailVerifications;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.EmailVerifications.RequestEmailVerification;

/// <summary>
/// Отправляет письмо со ссылкой подтверждения email.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок зафиксирован: пользователь → уже подтверждён → почта настроена →
/// кулдаун → транзакция «отозвать активный токен и вставить новый» → отправка письма
/// **после** коммита.
/// </para>
/// <para>
/// Отправка после коммита, а не внутри транзакции: письмо нельзя откатить, а строка токена
/// должна существовать, когда пользователь перейдёт по ссылке. Сбой отправки не откатывает
/// токен — пользователь повторит отправку через кулдаун.
/// </para>
/// <para>
/// В лог попадают только идентификатор пользователя и коды ошибок: адрес получателя и
/// открытый токен — персональные данные и секрет.
/// </para>
/// </remarks>
public sealed class RequestEmailVerificationHandler
    : ICommandHandler<RequestEmailVerificationCommand>
{
    private const string UNAVAILABLE_MESSAGE =
        "Email verification is not configured on this server.";

    private const string COOLDOWN_MESSAGE =
        "The verification email has already been sent. Try again in a minute.";

    private const string SEND_FAILED_MESSAGE =
        "The verification email could not be sent.";

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly AppOptions _appOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestEmailVerificationHandler> _logger;

    public RequestEmailVerificationHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        AppOptions appOptions,
        TimeProvider timeProvider,
        ILogger<RequestEmailVerificationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(emailSender);
        ArgumentNullException.ThrowIfNull(appOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _users = users;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _appOptions = appOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет состояние пользователя, выпускает новый токен и отправляет письмо.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RequestEmailVerificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userResult = await _users.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        // Уже подтверждён — письмо не нужно. Проверка стоит до конфигурации почты:
        // подтверждённый пользователь не должен получать 409 на пустом сервере.
        if (user.IsEmailVerified)
        {
            return UnitResult.Success<Failure>();
        }

        var publicUrl = _appOptions.PublicUrl?.TrimEnd('/');

        if (!_emailSender.IsAvailable || string.IsNullOrWhiteSpace(publicUrl))
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Auth.EmailVerificationUnavailable,
                UNAVAILABLE_MESSAGE));
        }

        var now = _timeProvider.GetUtcNow();

        var activeResult = await _tokens.GetActiveByUserIdAsync(user.Id, cancellationToken);
        if (activeResult.IsFailure)
        {
            return activeResult.Error;
        }

        // Кулдаун считается по IssuedAt — бизнес-времени выдачи, а не по CreatedAt строки.
        // Ровно через 60 секунд отправка разрешена: сравнение строгое.
        if (activeResult.Value is { } activeToken &&
            activeToken.IssuedAt > now - EmailVerificationLifetime.RESEND_COOLDOWN)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Auth.EmailVerificationCooldown,
                COOLDOWN_MESSAGE));
        }

        var rawToken = EmailVerificationTokenGenerator.Generate();

        var created = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationTokenGenerator.ComputeHash(rawToken),
            now,
            now + EmailVerificationLifetime.TOKEN_LIFETIME,
            now);

        if (created.IsFailure)
        {
            return created.Error;
        }

        var token = created.Value;

        // Отзыв предыдущего активного и вставка нового — одна транзакция: в базе не должно
        // остаться двух активных строк. Повторное чтение внутри транзакции закрывает окно
        // между проверкой кулдауна и записью; частичный уникальный индекс — последняя защита.
        var persisted = await _unitOfWork.ExecuteAsync(async transactionToken =>
        {
            var existingResult = await _tokens.GetActiveByUserIdAsync(user.Id, transactionToken);
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

            return await _tokens.AddAsync(token, transactionToken);
        }, cancellationToken);

        if (persisted.IsFailure)
        {
            return persisted.Error;
        }

        // Ссылка собирается ровно в одном месте; PublicUrl уже проверен на пустоту.
        var link = $"{publicUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        var message = EmailVerificationMessage.Create(
            user.Email,
            user.FirstName,
            link,
            (int)EmailVerificationLifetime.TOKEN_LIFETIME.TotalHours);

        var sent = await _emailSender.SendAsync(message, cancellationToken);
        if (sent.IsFailure)
        {
            _logger.LogError(
                "Failed to send the email verification message to user {UserId}: {ErrorCodes}.",
                user.Id,
                string.Join(", ", sent.Error.Errors.Select(error => error.Code)));

            return new Failure(Error.Failure(
                ApiErrorCodes.Auth.EmailVerificationSendFailed,
                SEND_FAILED_MESSAGE));
        }

        _logger.LogInformation(
            "Email verification message has been sent to user {UserId}.",
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
