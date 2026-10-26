using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.Passwords;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.LoginUser;

/// <summary>
/// Проверяет учётные данные и фиксирует вход.
/// </summary>
/// <remarks>
/// <para>
/// Несуществующий адрес и неверный пароль дают одну и ту же ошибку, чтобы клиент не мог
/// перебором выяснить, какие адреса зарегистрированы. Для несуществующего адреса всё равно
/// выполняется проверка по фиктивному хешу — иначе время ответа выдавало бы наличие аккаунта.
/// </para>
/// <para>
/// Защита от распределённого перебора живёт на аккаунте (R-21): каждая неудача подряд
/// увеличивает <c>User.FailedLoginAttempts</c>, после
/// <c>Constants.MAX_FAILED_LOGIN_ATTEMPTS</c> вход блокируется на растущую задержку.
/// «Сейчас» берётся из <see cref="TimeProvider"/>: в тестах время управляемое.
/// </para>
/// <para>
/// <c>auth.login.lockedOut</c> возвращается только при верном пароле, поэтому выяснить
/// существование адреса перебором нельзя: при неверном пароле заблокированный аккаунт
/// отвечает так же, как обычный, — <c>auth.login.invalidCredentials</c>.
/// </para>
/// </remarks>
public class LoginUserHandler : ICommandHandler<AuthenticatedUser, LoginUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly IPasswordService _passwords;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LoginUserHandler> _logger;

    public LoginUserHandler(
        IUserRepository repository,
        IPasswordService passwords,
        TimeProvider timeProvider,
        ILogger<LoginUserHandler> logger)
    {
        _repository = repository;
        _passwords = passwords;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Находит пользователя по адресу, сверяет пароль и обновляет время последнего входа.
    /// </summary>
    public async Task<Result<AuthenticatedUser, Failure>> Handle(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        // Потолок длины — единственное правило политики, применимое ко входу: пароли
        // существующих пользователей могли быть созданы до её появления, а композиционные
        // правила при проверке уже сохранённого пароля ничего не значат. Проверка дешёвая
        // и стоит до PBKDF2, чтобы огромный пароль не жёг CPU.
        if (command.Password is { Length: > PasswordPolicy.MAX_LENGTH })
        {
            _logger.LogInformation(
                "Login attempt rejected: the password exceeds the maximum length.");

            return InvalidCredentials();
        }

        var userResult = await _repository.GetByEmailAsync(command.Email, cancellationToken);
        if (userResult.IsFailure)
        {
            _passwords.VerifyDummy(command.Password);

            _logger.LogInformation("Login attempt for an unknown email.");

            return InvalidCredentials();
        }

        var user = userResult.Value;
        var now = _timeProvider.GetUtcNow();

        var verification = _passwords.Verify(user.PasswordHash, command.Password);
        if (verification == PasswordVerification.Failed)
        {
            // Заблокированный аккаунт не увеличивает счётчик и не продлевает блокировку:
            // иначе атакующий, продолжая попытки, держал бы владельца заблокированным вечно.
            if (user.IsLockedOut(now))
            {
                _logger.LogInformation(
                    "Login attempt with an invalid password for the locked user {UserId}.",
                    user.Id);

                return InvalidCredentials();
            }

            user.RegisterFailedLogin(now);

            var recorded = await _repository.UpdateAsync(user, cancellationToken);
            if (recorded.IsFailure)
            {
                // Счётчик — вспомогательная запись: неудачный вход не должен превращаться
                // в 500 и не должен выдавать клиенту существование аккаунта.
                _logger.LogWarning(
                    "Failed to persist a failed login attempt for user {UserId}: {Error}.",
                    user.Id,
                    recorded.Error);
            }

            _logger.LogInformation(
                "Login attempt with an invalid password for user {UserId}; failed attempts: {FailedAttempts}.",
                user.Id,
                user.FailedLoginAttempts);

            return InvalidCredentials();
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for deactivated user {UserId}.", user.Id);

            return new Failure(Error.Unauthorized(
                "auth.login.inactive",
                "The account is deactivated."));
        }

        if (user.IsLockedOut(now))
        {
            _logger.LogWarning(
                "Login attempt for user {UserId} while the account is locked out until {LockoutEnd}.",
                user.Id,
                user.LockoutEnd);

            return new Failure(Error.Unauthorized(
                ApiErrorCodes.Auth.LockedOut,
                "The account is temporarily locked after repeated failed sign-in attempts."));
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(_passwords.Hash(command.Password));
        }

        user.ResetFailedLogin();
        user.RecordLogin();

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("User {UserId} has logged in.", user.Id);

        return AuthenticatedUser.From(user);
    }

    private static Failure InvalidCredentials()
        => new(Error.Unauthorized(
            "auth.login.invalidCredentials",
            "Invalid email or password."));
}
