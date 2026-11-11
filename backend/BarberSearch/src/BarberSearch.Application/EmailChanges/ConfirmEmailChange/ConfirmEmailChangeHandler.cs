using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailChanges;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.EmailChanges.ConfirmEmailChange;

/// <summary>
/// Меняет адрес электронной почты по одноразовой ссылке и завершает все сессии пользователя.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок: формат токена → поиск по хешу → пользователь → идемпотентность
/// (адрес уже применён → <c>204</c>) → активность токена → занятость адреса → одна транзакция
/// «сменить адрес, потребить токен, отозвать все refresh-токены».
/// </para>
/// <para>
/// Смена адреса ротирует <c>SecurityStamp</c> в домене, поэтому все выданные cookie и
/// access-токены перестают проходить проверку, а отзыв refresh-токенов не даёт продлить
/// сессию. Автоматического повторного входа нет: страница подтверждения предлагает войти
/// заново новым адресом.
/// </para>
/// <para>
/// <see cref="Domain.Users.User.IsActive"/> не проверяется: владение адресом не зависит от
/// состояния аккаунта, а деактивированный пользователь всё равно не войдёт. Внешне это
/// выглядит как успешная смена, и такое поведение осознанно совпадает с подтверждением email.
/// </para>
/// <para>
/// Гонка двух подтверждений: второй <c>SaveChanges</c> может проиграть оптимистическую
/// блокировку. Тогда пользователь перечитывается, и если адрес уже применён, ответ — успех,
/// а не <c>500</c>. Проигранная гонка за занятый адрес переводится из
/// <c>user.update.uniqueViolation</c> в <c>409 user.changeEmail.emailTaken</c>.
/// </para>
/// </remarks>
public sealed class ConfirmEmailChangeHandler
    : ICommandHandler<ConfirmEmailChangeCommand>
{
    /// <summary>
    /// Код репозитория «токена с таким хешем нет».
    /// </summary>
    private const string TOKEN_NOT_FOUND_CODE = "emailChangeToken.notFound";

    /// <summary>
    /// Код сохранения пользователя при нарушении уникального индекса <c>ux_users_email</c>.
    /// </summary>
    private const string USER_UNIQUE_VIOLATION_CODE = "user.update.uniqueViolation";

    private const string INVALID_MESSAGE =
        "The email change link is invalid or has expired.";

    private const string EMAIL_TAKEN_MESSAGE =
        "This email address is already in use by another account.";

    private readonly IUserRepository _users;
    private readonly IEmailChangeTokenRepository _tokens;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConfirmEmailChangeHandler> _logger;

    public ConfirmEmailChangeHandler(
        IUserRepository users,
        IEmailChangeTokenRepository tokens,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ConfirmEmailChangeHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(refreshTokens);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _users = users;
        _tokens = tokens;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет ссылку и атомарно меняет адрес и завершает сессии.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ConfirmEmailChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Формат проверяется до SHA-256: мусор не должен тратить хеш и ходить в базу.
        if (!EmailChangeTokenGenerator.IsValidToken(command.Token))
        {
            return Invalid();
        }

        var hash = EmailChangeTokenGenerator.ComputeHash(command.Token);

        var tokenResult = await _tokens.GetByHashAsync(hash, cancellationToken);
        if (tokenResult.IsFailure)
        {
            // Неизвестный токен неотличим от просроченного/использованного. Любую другую
            // ошибку репозитория не маскируем под 400: это инфраструктурный сбой.
            return IsOnly(tokenResult.Error, TOKEN_NOT_FOUND_CODE)
                ? Invalid()
                : tokenResult.Error;
        }

        var token = tokenResult.Value;

        var userResult = await _users.GetByIdAsync(token.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return IsOnly(userResult.Error, ApiErrorCodes.User.NotFound)
                ? Invalid()
                : userResult.Error;
        }

        var user = userResult.Value;

        // Идемпотентность: повторный клик по ссылке после успешной смены — успех.
        // Проверка стоит раньше срока токена: старая ссылка на применённый адрес не должна
        // внезапно отвечать 400.
        if (string.Equals(user.Email, token.NewEmail, StringComparison.Ordinal))
        {
            return UnitResult.Success<Failure>();
        }

        var now = _timeProvider.GetUtcNow();

        if (token.ConsumedAt is not null ||
            token.RevokedAt is not null ||
            !token.IsActive(now))
        {
            return Invalid();
        }

        var taken = await _users.EmailExistsAsync(token.NewEmail, cancellationToken);
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

        var result = await _unitOfWork.ExecuteAsync(async transactionToken =>
        {
            var changed = user.ChangeEmail(token.NewEmail);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            var consumed = token.Consume(now);
            if (consumed.IsFailure)
            {
                return consumed.Error;
            }

            // Пользователь, токен и отзыв refresh-токенов — в одной транзакции: частичного
            // результата «адрес сменён, старые сессии живы» быть не должно.
            var savedUser = await _users.UpdateAsync(user, transactionToken);
            if (savedUser.IsFailure)
            {
                return savedUser.Error;
            }

            var savedToken = await _tokens.UpdateAsync(token, transactionToken);
            if (savedToken.IsFailure)
            {
                return savedToken.Error;
            }

            return await _refreshTokens.RevokeAllForUserAsync(
                user.Id,
                now,
                transactionToken);
        }, cancellationToken);

        if (result.IsFailure)
        {
            return await HandleFailureAsync(token, result.Error, cancellationToken);
        }

        _logger.LogInformation(
            "User {UserId} has changed the email address via a confirmation link; " +
            "all refresh tokens have been revoked.",
            user.Id);

        return UnitResult.Success<Failure>();
    }

    /// <summary>
    /// Переводит проигранные гонки в бизнес-ответы, а не в <c>500</c>.
    /// </summary>
    /// <remarks>
    /// Занятый адрес — <c>409</c> (уникальный индекс сработал позже нашей проверки).
    /// Конкурентное подтверждение — успех, если адрес уже применён: клиент не должен видеть
    /// ошибку на повторный клик.
    /// </remarks>
    private async Task<UnitResult<Failure>> HandleFailureAsync(
        EmailChangeToken token,
        Failure failure,
        CancellationToken cancellationToken)
    {
        if (IsCode(failure, USER_UNIQUE_VIOLATION_CODE))
        {
            _logger.LogInformation(
                "Email change for user {UserId} lost the race for a taken address.",
                token.UserId);

            return new Failure(Error.Conflict(
                ApiErrorCodes.User.ChangeEmailEmailTaken,
                EMAIL_TAKEN_MESSAGE));
        }

        if (!IsConcurrencyConflict(failure))
        {
            return failure;
        }

        var currentResult = await _users.GetByIdAsync(token.UserId, cancellationToken);
        if (currentResult.IsSuccess &&
            string.Equals(currentResult.Value.Email, token.NewEmail, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Concurrent email change for user {UserId} already succeeded.",
                token.UserId);

            return UnitResult.Success<Failure>();
        }

        return failure;
    }

    private static bool IsCode(Failure failure, string code)
        => failure.Errors.Any(error => error.Code == code);

    private static bool IsOnly(Failure failure, string code)
        => failure.Errors.All(error => error.Code == code);

    private static bool IsConcurrencyConflict(Failure failure)
        => failure.Errors.Any(error => error.Code.EndsWith(
            ".concurrencyConflict",
            StringComparison.Ordinal));

    private static Failure Invalid()
        => new(Error.Validation(
            ApiErrorCodes.Auth.EmailChangeInvalid,
            INVALID_MESSAGE));
}
