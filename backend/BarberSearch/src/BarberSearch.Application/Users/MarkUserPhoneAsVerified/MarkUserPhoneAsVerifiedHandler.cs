using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.MarkUserPhoneAsVerified;

/// <summary>
/// Обработчик команды <see cref="MarkUserPhoneAsVerifiedCommand"/>.
/// </summary>
/// <remarks>
/// Отсутствие номера телефона проверяет домен: подтверждать нечего, пока телефон не задан.
/// </remarks>
public class MarkUserPhoneAsVerifiedHandler : ICommandHandler<MarkUserPhoneAsVerifiedCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<MarkUserPhoneAsVerifiedHandler> _logger;

    public MarkUserPhoneAsVerifiedHandler(IUserRepository repository, ILogger<MarkUserPhoneAsVerifiedHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, помечает телефон подтверждённым и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        MarkUserPhoneAsVerifiedCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var verified = user.MarkPhoneAsVerified();
        if (verified.IsFailure)
        {
            return verified.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("User {UserId} phone has been marked as verified.", user.Id);

        return UnitResult.Success<Failure>();
    }
}
