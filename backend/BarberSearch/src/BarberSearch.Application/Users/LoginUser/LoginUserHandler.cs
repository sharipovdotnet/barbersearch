using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.LoginUser;

/// <summary>
/// Проверяет учётные данные и фиксирует вход.
/// </summary>
/// <remarks>
/// Несуществующий адрес и неверный пароль дают одну и ту же ошибку, чтобы клиент не мог
/// перебором выяснить, какие адреса зарегистрированы. Для несуществующего адреса всё равно
/// выполняется проверка по фиктивному хешу — иначе время ответа выдавало бы наличие аккаунта.
/// </remarks>
public class LoginUserHandler : ICommandHandler<AuthenticatedUser, LoginUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly IPasswordService _passwords;
    private readonly ILogger<LoginUserHandler> _logger;

    public LoginUserHandler(
        IUserRepository repository,
        IPasswordService passwords,
        ILogger<LoginUserHandler> logger)
    {
        _repository = repository;
        _passwords = passwords;
        _logger = logger;
    }

    /// <summary>
    /// Находит пользователя по адресу, сверяет пароль и обновляет время последнего входа.
    /// </summary>
    public async Task<Result<AuthenticatedUser, Failure>> Handle(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByEmailAsync(command.Email, cancellationToken);
        if (userResult.IsFailure)
        {
            _passwords.VerifyDummy(command.Password);

            _logger.LogInformation("Login attempt for an unknown email.");

            return InvalidCredentials();
        }

        var user = userResult.Value;

        var verification = _passwords.Verify(user.PasswordHash, command.Password);
        if (verification == PasswordVerification.Failed)
        {
            _logger.LogInformation(
                "Login attempt with an invalid password for user {UserId}.",
                user.Id);

            return InvalidCredentials();
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for deactivated user {UserId}.", user.Id);

            return new Failure(Error.Unauthorized(
                "auth.login.inactive",
                "The account is deactivated."));
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(_passwords.Hash(command.Password));
        }

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
