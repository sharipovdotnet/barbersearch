using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.RecordUserLogin;

/// <summary>
/// Обработчик команды <see cref="RecordUserLoginCommand"/>.
/// </summary>
/// <remarks>
/// Метка времени ставится доменом (<c>DateTimeOffset.UtcNow</c>), обработчик её не вычисляет.
/// </remarks>
public class RecordUserLoginHandler : ICommandHandler<RecordUserLoginCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<RecordUserLoginHandler> _logger;

    public RecordUserLoginHandler(IUserRepository repository, ILogger<RecordUserLoginHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, обновляет время последнего входа и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RecordUserLoginCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var recorded = user.RecordLogin();
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("User {UserId} login has been recorded.", user.Id);

        return UnitResult.Success<Failure>();
    }
}
