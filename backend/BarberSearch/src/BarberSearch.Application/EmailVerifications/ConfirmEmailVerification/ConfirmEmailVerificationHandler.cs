using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.EmailVerifications.ConfirmEmailVerification;

/// <summary>
/// Подтверждает адрес электронной почты по одноразовому токену.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок: формат → токен по хешу → пользователь → уже подтверждён → токен
/// истёк/использован → одна транзакция «подтвердить пользователя и потребить токен».
/// </para>
/// <para>
/// <see cref="Domain.Users.User.IsActive"/> не проверяется: владение адресом не зависит от
/// состояния аккаунта, а деактивированный пользователь всё равно не войдёт.
/// </para>
/// <para>
/// Гонка двух подтверждений: второй <c>SaveChanges</c> может проиграть оптимистическую
/// блокировку. Тогда пользователь перечитывается, и если он уже подтверждён, ответ — успех,
/// а не <c>500</c>.
/// </para>
/// </remarks>
public sealed class ConfirmEmailVerificationHandler
    : ICommandHandler<ConfirmEmailVerificationCommand>
{
    private const string INVALID_MESSAGE =
        "The email verification link is invalid or has expired.";

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConfirmEmailVerificationHandler> _logger;

    public ConfirmEmailVerificationHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ConfirmEmailVerificationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _users = users;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет токен и подтверждает адрес.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ConfirmEmailVerificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Формат проверяется до SHA-256: мусор не должен тратить хеш и ходить в базу.
        if (!EmailVerificationTokenGenerator.IsValidToken(command.Token))
        {
            return Invalid();
        }

        var hash = EmailVerificationTokenGenerator.ComputeHash(command.Token);

        var tokenResult = await _tokens.GetByHashAsync(hash, cancellationToken);
        if (tokenResult.IsFailure)
        {
            return Invalid();
        }

        var token = tokenResult.Value;

        var userResult = await _users.GetByIdAsync(token.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            // Токен ссылается на несуществующего пользователя (FK с ON DELETE CASCADE
            // обычно не даёт такой строки): для клиента это та же недействительная ссылка.
            return Invalid();
        }

        var user = userResult.Value;

        // Идемпотентность: повторный клик и уже подтверждённый адрес — успех. Проверка
        // стоит раньше срока токена: старая ссылка на подтверждённый адрес не должна
        // внезапно отвечать 400.
        if (user.IsEmailVerified)
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

        var result = await _unitOfWork.ExecuteAsync(async transactionToken =>
        {
            var verified = user.MarkEmailAsVerified();
            if (verified.IsFailure)
            {
                return verified.Error;
            }

            var consumed = token.Consume(now);
            if (consumed.IsFailure)
            {
                return consumed.Error;
            }

            // Пользователь и токен сохраняются в одной транзакции: либо адрес подтверждён
            // и токен использован, либо не изменено ничего.
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

            return UnitResult.Success<Failure>();
        }, cancellationToken);

        if (result.IsFailure)
        {
            return await HandleFailureAsync(token.UserId, result.Error, cancellationToken);
        }

        _logger.LogInformation(
            "Email of user {UserId} has been confirmed via a verification link.",
            user.Id);

        return UnitResult.Success<Failure>();
    }

    /// <summary>
    /// Проигранная гонка двух подтверждений не должна превращаться в <c>500</c>: если
    /// параллельный запрос уже подтвердил адрес, для клиента это успех.
    /// </summary>
    private async Task<UnitResult<Failure>> HandleFailureAsync(
        Guid userId,
        Failure failure,
        CancellationToken cancellationToken)
    {
        if (!IsConcurrencyConflict(failure))
        {
            return failure;
        }

        var currentResult = await _users.GetByIdAsync(userId, cancellationToken);
        if (currentResult.IsSuccess && currentResult.Value.IsEmailVerified)
        {
            _logger.LogInformation(
                "Concurrent email verification for user {UserId} already succeeded.",
                userId);

            return UnitResult.Success<Failure>();
        }

        return failure;
    }

    private static bool IsConcurrencyConflict(Failure failure)
        => failure.Errors.Any(error => error.Code.EndsWith(
            ".concurrencyConflict",
            StringComparison.Ordinal));

    private static Failure Invalid()
        => new(Error.Validation(
            ApiErrorCodes.Auth.EmailVerificationInvalid,
            INVALID_MESSAGE));
}
