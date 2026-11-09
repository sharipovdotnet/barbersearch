using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Emails;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailOutbox;
using BarberSearch.Domain.EmailVerifications;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.EmailVerifications.RequestEmailVerification;

/// <summary>
/// Выпускает одноразовый токен подтверждения email и кладёт письмо в outbox.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок зафиксирован: пользователь → уже подтверждён → почта настроена →
/// кулдаун → одна транзакция «отозвать активный токен, вставить новый и положить письмо
/// в outbox».
/// </para>
/// <para>
/// Письмо больше не отправляется из запроса. Строка <c>email_outbox</c> пишется в той же
/// транзакции, что и токен, поэтому письмо не теряется, даже когда SMTP недоступен; отправляет
/// его фоновый диспетчер с ретраями (at-least-once, дубликат допустим). Сбой отправки не
/// откатывает токен — письмо уйдёт, когда транспорт восстановится.
/// </para>
/// <para>
/// В лог попадают только идентификатор пользователя и коды ошибок: адрес получателя,
/// открытый токен и тело письма — персональные данные и секрет.
/// </para>
/// </remarks>
public sealed class RequestEmailVerificationHandler
    : ICommandHandler<RequestEmailVerificationCommand>
{
    private const string UNAVAILABLE_MESSAGE =
        "Email verification is not configured on this server.";

    private const string COOLDOWN_MESSAGE =
        "The verification email has already been sent. Try again in a minute.";

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IEmailOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSenderAvailability _emailAvailability;
    private readonly AppOptions _appOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestEmailVerificationHandler> _logger;

    public RequestEmailVerificationHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IEmailOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        IEmailSenderAvailability emailAvailability,
        AppOptions appOptions,
        TimeProvider timeProvider,
        ILogger<RequestEmailVerificationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(emailAvailability);
        ArgumentNullException.ThrowIfNull(appOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _users = users;
        _tokens = tokens;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _emailAvailability = emailAvailability;
        _appOptions = appOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет состояние пользователя, выпускает новый токен и ставит письмо в outbox.
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

        if (!_emailAvailability.IsAvailable || string.IsNullOrWhiteSpace(publicUrl))
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

        // Ссылка собирается ровно в одном месте; PublicUrl уже проверен на пустоту.
        var link = $"{publicUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        var content = EmailVerificationMessage.Create(
            user.Email,
            user.FirstName,
            link,
            (int)EmailVerificationLifetime.TOKEN_LIFETIME.TotalHours);

        // Ключ дедупликации — id токена: одно письмо на одну выданную строку токена.
        // Повторная переотправка выпускает новый токен и, значит, новое письмо.
        var queued = EmailOutboxMessage.Create(
            token.Id.ToString("D"),
            content.To,
            content.Subject,
            content.HtmlBody,
            content.TextBody,
            now);

        if (queued.IsFailure)
        {
            return queued.Error;
        }

        // Отзыв предыдущего активного, вставка нового токена и строка outbox — одна транзакция:
        // в базе не должно остаться двух активных строк, а письмо не должно пережить откат
        // токена (или наоборот). Повторное чтение внутри транзакции закрывает окно между
        // проверкой кулдауна и записью; частичный уникальный индекс — последняя защита.
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

            var addedToken = await _tokens.AddAsync(token, transactionToken);
            if (addedToken.IsFailure)
            {
                return addedToken.Error;
            }

            return await _outbox.AddAsync(queued.Value, transactionToken);
        }, cancellationToken);

        if (persisted.IsFailure)
        {
            return persisted.Error;
        }

        // Письмо ещё не отправлено — его отправит диспетчер. В лог не попадают ни адрес,
        // ни токен: только пользователь и факт постановки в очередь.
        _logger.LogInformation(
            "Email verification message for user {UserId} has been queued in the outbox.",
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
