using BarberSearch.Application.Abstractions;
using BarberSearch.Application.EmailVerifications;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.PasswordResets;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PasswordResets.RequestPasswordReset;

/// <summary>
/// Выпускает одноразовый токен сброса пароля и отправляет письмо со ссылкой.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок зафиксирован §4.3 спеки: глобальная конфигурация → формат адреса →
/// пользователь → активность → кулдаун → одна транзакция «отозвать активный токен и вставить
/// новый» → отправка письма **после** коммита.
/// </para>
/// <para>
/// Анти-энумерация важнее честности ошибок: для несуществующего, неактивного, пустого и
/// кривого адреса ответ одинаковый — <c>Ok</c> (HTTP <c>204</c>) без письма. Единственный отказ
/// здесь — глобальный <c>409 auth.passwordReset.unavailable</c>, когда почта или
/// <c>App:PublicUrl</c> не настроены; он возвращается **до** поиска пользователя.
/// </para>
/// <para>
/// Кулдаун и сбой SMTP тихие: иначе существующий адрес отличался бы от несуществующего.
/// Сбой отправки не откатывает токен — через кулдаун можно запросить новое письмо.
/// </para>
/// <para>
/// В лог попадают только идентификатор пользователя и коды ошибок: адрес получателя,
/// открытый токен, его хеш и пароль — персональные данные и секрет.
/// </para>
/// </remarks>
public sealed class RequestPasswordResetHandler
    : ICommandHandler<RequestPasswordResetCommand>
{
    /// <summary>
    /// Внутренний код проигранной гонки за активную строку.
    /// </summary>
    private const string ISSUE_CONFLICT_CODE = "passwordResetToken.issue.conflict";

    private const string UNAVAILABLE_MESSAGE =
        "Password reset by email is not configured on this server.";

    private readonly IUserRepository _users;
    private readonly IPasswordResetTokenRepository _tokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _emailOptions;
    private readonly AppOptions _appOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestPasswordResetHandler> _logger;

    public RequestPasswordResetHandler(
        IUserRepository users,
        IPasswordResetTokenRepository tokens,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        EmailOptions emailOptions,
        AppOptions appOptions,
        TimeProvider timeProvider,
        ILogger<RequestPasswordResetHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(emailSender);
        ArgumentNullException.ThrowIfNull(emailOptions);
        ArgumentNullException.ThrowIfNull(appOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _users = users;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
        _appOptions = appOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет адрес и состояние пользователя, выпускает новый токен и отправляет письмо.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var publicUrl = _appOptions.PublicUrl?.TrimEnd('/');

        // Глобальное состояние сервера проверяется до поиска пользователя: иначе 409
        // получали бы только существующие адреса и стал бы оракулом существования.
        if (!_emailOptions.IsConfigured || string.IsNullOrWhiteSpace(publicUrl))
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Auth.PasswordResetUnavailable,
                UNAVAILABLE_MESSAGE));
        }

        var email = User.NormalizeEmail(command.Email);

        // Пустой и слишком длинный адрес отсекаются до репозитория и хеширования:
        // ответ всё равно Ok, но запрос в базу и письмо не нужны.
        if (email.Length == 0 || email.Length > Constants.MAX_EMAIL_LENGTH)
        {
            return UnitResult.Success<Failure>();
        }

        var userResult = await _users.GetByEmailAsync(email, cancellationToken);
        if (userResult.IsFailure)
        {
            // «Нет пользователя» и «адрес не прошёл проверку» неотличимы от успеха.
            // Любую другую ошибку репозитория не маскируем: это 500, а не тихий Ok.
            return IsUserLookupMiss(userResult.Error)
                ? UnitResult.Success<Failure>()
                : userResult.Error;
        }

        var user = userResult.Value;

        // Неактивный аккаунт не восстанавливается: письма нет, ответ такой же, как для
        // несуществующего адреса.
        if (!user.IsActive)
        {
            return UnitResult.Success<Failure>();
        }

        var now = _timeProvider.GetUtcNow();

        var activeResult = await _tokens.GetActiveByUserIdAsync(user.Id, cancellationToken);
        if (activeResult.IsFailure)
        {
            return activeResult.Error;
        }

        // Кулдаун считается по IssuedAt — бизнес-времени выдачи, а не по CreatedAt строки.
        // Сравнение строгое: ровно через 60 секунд отправка уже разрешена.
        if (activeResult.Value is { } activeToken &&
            activeToken.IssuedAt > now - PasswordResetLifetime.RESEND_COOLDOWN)
        {
            return UnitResult.Success<Failure>();
        }

        var rawToken = PasswordResetTokenGenerator.Generate();

        var created = PasswordResetToken.Create(
            user.Id,
            PasswordResetTokenGenerator.ComputeHash(rawToken),
            now,
            now + PasswordResetLifetime.TOKEN_LIFETIME,
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
            // Проигранная гонка двух переотправок — не ошибка клиента и не 500:
            // параллельный запрос уже выпустил письмо, для клиента это тихий успех.
            if (IsActiveTokenRace(persisted.Error))
            {
                _logger.LogInformation(
                    "Concurrent password reset request for user {UserId} won the race.",
                    user.Id);

                return UnitResult.Success<Failure>();
            }

            return persisted.Error;
        }

        // Ссылка собирается ровно в одном месте; PublicUrl уже проверен на пустоту.
        var link = $"{publicUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        var message = PasswordResetMessage.Create(
            user.Email,
            user.FirstName,
            link,
            (int)PasswordResetLifetime.TOKEN_LIFETIME.TotalMinutes);

        var sent = await _emailSender.SendAsync(message, cancellationToken);
        if (sent.IsFailure)
        {
            // Сбой SMTP — тихий: 500 только для существующего адреса был бы оракулом.
            // Токен остаётся в базе, повторная отправка возможна после кулдауна.
            _logger.LogWarning(
                "Failed to send the password reset message to user {UserId}: {ErrorCodes}.",
                user.Id,
                string.Join(", ", sent.Error.Errors.Select(error => error.Code)));

            return UnitResult.Success<Failure>();
        }

        _logger.LogInformation(
            "Password reset message has been sent to user {UserId}.",
            user.Id);

        return UnitResult.Success<Failure>();
    }

    private static bool IsUserLookupMiss(Failure failure)
        => failure.Errors.All(error =>
            error.Code is ApiErrorCodes.User.NotFound or ApiErrorCodes.User.EmailInvalid);

    private static bool IsActiveTokenRace(Failure failure)
        => failure.Errors.Any(error =>
            error.Code == ISSUE_CONFLICT_CODE ||
            error.Code.EndsWith(".concurrencyConflict", StringComparison.Ordinal));
}
