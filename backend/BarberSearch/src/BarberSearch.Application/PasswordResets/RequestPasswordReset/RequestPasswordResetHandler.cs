using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Emails;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.EmailOutbox;
using BarberSearch.Domain.PasswordResets;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PasswordResets.RequestPasswordReset;

/// <summary>
/// Выпускает одноразовый токен сброса пароля и кладёт письмо со ссылкой в outbox.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок зафиксирован §4.3 спеки: глобальная конфигурация → формат адреса →
/// пользователь → активность → кулдаун → одна транзакция «отозвать активный токен, вставить
/// новый и положить письмо в outbox».
/// </para>
/// <para>
/// Анти-энумерация важнее честности ошибок: для несуществующего, неактивного, пустого и
/// кривого адреса ответ одинаковый — <c>Ok</c> (HTTP <c>204</c>) без письма. Единственный отказ
/// здесь — глобальный <c>409 auth.passwordReset.unavailable</c>, когда почта или
/// <c>App:PublicUrl</c> не настроены; он возвращается **до** поиска пользователя.
/// </para>
/// <para>
/// Письмо не отправляется из запроса: оно попадает в <c>email_outbox</c> в той же транзакции,
/// что и токен, и уходит через фоновый диспетчер. Поэтому недоступный SMTP больше не влияет
/// на ответ — и не может стать оракулом существования адреса.
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
    private readonly IEmailOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSenderAvailability _emailAvailability;
    private readonly AppOptions _appOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestPasswordResetHandler> _logger;

    public RequestPasswordResetHandler(
        IUserRepository users,
        IPasswordResetTokenRepository tokens,
        IEmailOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        IEmailSenderAvailability emailAvailability,
        AppOptions appOptions,
        TimeProvider timeProvider,
        ILogger<RequestPasswordResetHandler> logger)
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
    /// Проверяет адрес и состояние пользователя, выпускает новый токен и ставит письмо в outbox.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var publicUrl = _appOptions.PublicUrl?.TrimEnd('/');

        // Глобальное состояние сервера проверяется до поиска пользователя: иначе 409
        // получали бы только существующие адреса и стал бы оракулом существования.
        if (!_emailAvailability.IsAvailable || string.IsNullOrWhiteSpace(publicUrl))
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

        // Ссылка собирается ровно в одном месте; PublicUrl уже проверен на пустоту.
        var link = $"{publicUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        var content = PasswordResetMessage.Create(
            user.Email,
            user.FirstName,
            link,
            (int)PasswordResetLifetime.TOKEN_LIFETIME.TotalMinutes);

        // Ключ дедупликации — id токена: одно письмо на одну выданную строку токена.
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

        // Письмо ещё не отправлено — его отправит диспетчер. В лог не попадают ни адрес,
        // ни токен: только пользователь и факт постановки в очередь.
        _logger.LogInformation(
            "Password reset message for user {UserId} has been queued in the outbox.",
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
