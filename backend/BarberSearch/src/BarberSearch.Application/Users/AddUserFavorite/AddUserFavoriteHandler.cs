using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.AddUserFavorite;

/// <summary>
/// Обработчик команды <see cref="AddUserFavoriteCommand"/>.
/// </summary>
/// <remarks>
/// Правило «нельзя добавить в избранное собственный профиль барбера» живёт здесь, а не в домене:
/// агрегат <c>User</c> не знает, какой профиль барбера принадлежит ему (AGENT.md §7.6).
/// Повторное добавление отклоняет доменный метод.
/// </remarks>
public class AddUserFavoriteHandler : ICommandHandler<AddUserFavoriteCommand>
{
    private readonly IUserRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddUserFavoriteHandler> _logger;

    public AddUserFavoriteHandler(
        IUserRepository repository,
        ICurrentUser currentUser,
        ILogger<AddUserFavoriteHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, добавляет барбера в избранное и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddUserFavoriteCommand command,
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

        // Правило уровня Application: свой профиль барбера в избранное добавить нельзя,
        // поэтому доменный метод в этом случае не вызывается вовсе.
        if (user.BarberProfileId == command.BarberProfileId)
        {
            return new Failure(Error.Validation(
                "user.addFavorite.self",
                "A user cannot add their own barber profile to favorites."));
        }

        var added = user.AddFavorite(command.BarberProfileId);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Barber profile {BarberProfileId} has been added to user {UserId} favorites.",
            command.BarberProfileId,
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
