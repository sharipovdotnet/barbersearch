using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
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
    private readonly IBarberProfileRepository _profiles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddUserFavoriteHandler> _logger;

    public AddUserFavoriteHandler(
        IUserRepository repository,
        IBarberProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<AddUserFavoriteHandler> logger)
    {
        _repository = repository;
        _profiles = profiles;
        _unitOfWork = unitOfWork;
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

        // Ссылка на профиль барбера проверяется до вставки, в той же транзакции:
        // внешний ключ fk_favorite_barbers_barber_profiles_barber_profile_id не даст
        // сохранить избранное на несуществующий профиль (R-02).
        var result = await _unitOfWork.ExecuteAsync(async ct =>
        {
            var profileExists = await _profiles.ExistsAsync(command.BarberProfileId, ct);
            if (profileExists.IsFailure)
            {
                return profileExists.Error;
            }

            if (!profileExists.Value)
            {
                return new Failure(Error.NotFound(
                    "user.addFavorite.barberProfileNotFound",
                    $"Barber profile '{command.BarberProfileId}' was not found.",
                    command.BarberProfileId));
            }

            var added = user.AddFavorite(command.BarberProfileId);
            if (added.IsFailure)
            {
                return added.Error;
            }

            return await _repository.UpdateAsync(user, ct);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        _logger.LogInformation(
            "Barber profile {BarberProfileId} has been added to user {UserId} favorites.",
            command.BarberProfileId,
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
