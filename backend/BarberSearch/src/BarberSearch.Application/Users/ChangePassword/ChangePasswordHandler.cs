using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Application.Users.Passwords;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.ChangePassword;

/// <summary>
/// Меняет пароль после проверки текущего и отзывает все refresh-токены пользователя.
/// </summary>
/// <remarks>
/// Отзыв всех токенов, а не только текущего: если пароль сменили из-за подозрения на кражу,
/// остальные устройства не должны пережить смену. Access-токен доживает свои 15 минут —
/// это зафиксированное ограничение JWT (AGENT.md §17.7).
/// <para>
/// Новый хеш и отзыв токенов пишутся в одной транзакции: если отзыв не удался, пароль
/// остаётся прежним. Иначе смена пароля «из-за кражи» оставила бы живые refresh-токены (R-08).
/// </para>
/// <para>
/// Новый пароль проверяется <see cref="IPasswordPolicy"/> до хеширования. Обязательность,
/// длина и словарь проверяются до загрузки пользователя; совпадение с email/именем — после,
/// потому что эти данные хранятся в профиле. Текущий пароль политикой не проверяется:
/// к существующим паролям применяется только сверка хеша.
/// </para>
/// </remarks>
public class ChangePasswordHandler : ICommandHandler<ChangePasswordCommand>
{
    private readonly IUserRepository _users;
    private readonly IPasswordService _passwords;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChangePasswordHandler> _logger;

    public ChangePasswordHandler(
        IUserRepository users,
        IPasswordService passwords,
        IPasswordPolicy passwordPolicy,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ChangePasswordHandler> logger)
    {
        _users = users;
        _passwords = passwords;
        _passwordPolicy = passwordPolicy;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет текущий пароль, записывает новый хеш и гасит все refresh-токены.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        // Дешёвая часть политики (обязательность, длина, словарь) — до запроса к БД
        // и до PBKDF2: невалидный ввод не должен доходить ни до того, ни до другого.
        var composition = _passwordPolicy.Validate(command.NewPassword, "newPassword");
        if (composition is not null)
        {
            return composition;
        }

        var userResult = await _users.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        // Совпадение с email/именем проверяется после загрузки профиля: этих данных нет
        // в команде. Хеширование всё ещё впереди.
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

        if (_passwords.Verify(user.PasswordHash, command.CurrentPassword) == PasswordVerification.Failed)
        {
            _logger.LogInformation(
                "Password change rejected for user {UserId}: the current password is incorrect.",
                user.Id);

            return new Failure(Error.Validation(
                "auth.changePassword.invalidCurrentPassword",
                "The current password is incorrect.",
                nameof(command.CurrentPassword)));
        }

        // PBKDF2 считается до открытия транзакции: хеширование не должно держать соединение.
        var newPasswordHash = _passwords.Hash(command.NewPassword);

        // Доменное изменение и обе записи — в одной транзакции: частичного результата
        // «пароль сменён, токены живы» быть не должно. Любой Failure или исключение
        // откатывает и хеш, и отзыв.
        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var changed = user.ChangePasswordHash(newPasswordHash);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            var saved = await _users.UpdateAsync(user, token);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            var revoked = await _refreshTokens.RevokeAllForUserAsync(
                user.Id,
                _timeProvider.GetUtcNow(),
                token);

            if (revoked.IsFailure)
            {
                return revoked.Error;
            }

            return UnitResult.Success<Failure>();
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "User {UserId} changed the password; all refresh tokens have been revoked.",
                user.Id);
        }

        return result;
    }
}
