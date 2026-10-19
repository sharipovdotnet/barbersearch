using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.RemoveUserFavorite;

/// <summary>
/// Обработчик команды <see cref="RemoveUserFavoriteCommand"/>.
/// </summary>
/// <remarks>
/// Отсутствие барбера в избранном — это <c>Error.NotFound</c>, его возвращает доменный метод.
/// </remarks>
public class RemoveUserFavoriteHandler : ICommandHandler<RemoveUserFavoriteCommand>
{
    private readonly IUserRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveUserFavoriteHandler> _logger;

    public RemoveUserFavoriteHandler(
        IUserRepository repository,
        ICurrentUser currentUser,
        ILogger<RemoveUserFavoriteHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, убирает барбера из избранного и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveUserFavoriteCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var allowed = _currentUser.EnsureCanManage(user.Id);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var removed = user.RemoveFavorite(command.BarberProfileId);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Barber profile {BarberProfileId} has been removed from user {UserId} favorites.",
            command.BarberProfileId,
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
