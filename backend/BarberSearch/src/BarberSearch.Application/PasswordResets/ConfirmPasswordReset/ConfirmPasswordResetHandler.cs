using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Application.Users;
using BarberSearch.Application.Users.Passwords;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PasswordResets.ConfirmPasswordReset;

/// <summary>
/// Меняет пароль по одноразовому токену и завершает все сессии пользователя.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок §4.3: формат токена → токен по хешу → пользователь → активность →
/// срок/использование/отзыв → политика пароля → хеширование → одна транзакция
/// «потребить токен, сменить хеш, подтвердить email, сбросить блокировку, отозвать refresh».
/// </para>
/// <para>
/// Токен одноразовый и не идемпотентный: использованный, отозванный или просроченный токен
/// даёт <c>400 auth.passwordReset.invalid</c>, а не «уже сброшено». Гонка двух confirm
/// разрешается оптимистической блокировкой PostgreSQL: проигравший получает тот же
/// <c>400</c>, а не <c>500</c>.
/// </para>
/// <para>
/// <c>EmailOptions</c> и <c>App:PublicUrl</c> здесь не проверяются: письмо уже выдано,
/// повторная отправка не нужна. Активная блокировка входа не отказ — сброс пароля её снимает;
/// отказ только для деактивированного аккаунта (<c>!IsActive</c>).
/// </para>
/// </remarks>
public sealed class ConfirmPasswordResetHandler
    : ICommandHandler<ConfirmPasswordResetCommand>
{
    /// <summary>
    /// Код репозитория «токена с таким хешем нет».
    /// </summary>
    private const string TOKEN_NOT_FOUND_CODE = "passwordResetToken.notFound";

    private const string INVALID_MESSAGE =
        "The password reset link is invalid or has expired.";

    private readonly IUserRepository _users;
    private readonly IPasswordResetTokenRepository _tokens;
    private readonly IPasswordService _passwords;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConfirmPasswordResetHandler> _logger;

    public ConfirmPasswordResetHandler(
        IUserRepository users,
        IPasswordResetTokenRepository tokens,
        IPasswordService passwords,
        IPasswordPolicy passwordPolicy,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ConfirmPasswordResetHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(passwords);
        ArgumentNullException.ThrowIfNull(passwordPolicy);
        ArgumentNullException.ThrowIfNull(refreshTokens);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _users = users;
        _tokens = tokens;
        _passwords = passwords;
        _passwordPolicy = passwordPolicy;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет ссылку и пароль, затем атомарно меняет учётные данные и завершает сессии.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ConfirmPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Формат проверяется до SHA-256: мусор не должен тратить хеш и ходить в базу.
        if (!PasswordResetTokenGenerator.IsValidToken(command.Token))
        {
            return Invalid();
        }

        var hash = PasswordResetTokenGenerator.ComputeHash(command.Token);

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

        // Деактивированный аккаунт не восстанавливается: сброс не должен его реактивировать.
        if (!user.IsActive)
        {
            return Invalid();
        }

        var now = _timeProvider.GetUtcNow();

        // Одноразовость: использованный токен не «доигрывает» первый сброс, даже если
        // доменный Consume идемпотентен. Срок сравнивается строго.
        if (token.ConsumedAt is not null ||
            token.RevokedAt is not null ||
            !token.IsActive(now))
        {
            return Invalid();
        }

        // Политика проверяется до хеширования; токен при отказе не потребляется, поэтому
        // ссылку можно использовать повторно с валидным паролем.
        var composition = _passwordPolicy.Validate(command.NewPassword, "newPassword");
        if (composition is not null)
        {
            return composition;
        }

        var personalData = _passwordPolicy.Validate(
            command.NewPassword,
            user.Email,
            user.FirstName,
            user.LastName,
            "newPassword");

        if (personalData is not null)
        {
            return personalData;
        }

        // PBKDF2 считается до открытия транзакции: хеширование не должно держать соединение.
        var newPasswordHash = _passwords.Hash(command.NewPassword);

        // Все изменения — в одной транзакции: либо токен потреблён, пароль и stamp изменены,
        // email подтверждён, блокировка снята и refresh-токены отозваны, либо не изменено
        // ничего. Письмо здесь не отправляется и откатывать нечего.
        var result = await _unitOfWork.ExecuteAsync(async transactionToken =>
        {
            var consumed = token.Consume(now);
            if (consumed.IsFailure)
            {
                return consumed.Error;
            }

            var changed = user.ChangePasswordHash(newPasswordHash);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            var verified = user.MarkEmailAsVerified();
            if (verified.IsFailure)
            {
                return verified.Error;
            }

            // Сброс — легальный способ восстановить доступ: активная блокировка входа
            // снимается вместе со счётчиком неудач.
            var reset = user.ResetFailedLogin();
            if (reset.IsFailure)
            {
                return reset.Error;
            }

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
            // Проигранная гонка двух confirm: токен уже потреблён параллельным запросом.
            // Для клиента это недействительная ссылка, а не 500.
            return IsConcurrencyConflict(result.Error)
                ? Invalid()
                : result.Error;
        }

        _logger.LogInformation(
            "User {UserId} has reset the password via an email link; all refresh tokens have been revoked.",
            user.Id);

        return UnitResult.Success<Failure>();
    }

    private static bool IsOnly(Failure failure, string code)
        => failure.Errors.All(error => error.Code == code);

    private static bool IsConcurrencyConflict(Failure failure)
        => failure.Errors.Any(error => error.Code.EndsWith(
            ".concurrencyConflict",
            StringComparison.Ordinal));

    private static Failure Invalid()
        => new(Error.Validation(
            ApiErrorCodes.Auth.PasswordResetInvalid,
            INVALID_MESSAGE));
}
