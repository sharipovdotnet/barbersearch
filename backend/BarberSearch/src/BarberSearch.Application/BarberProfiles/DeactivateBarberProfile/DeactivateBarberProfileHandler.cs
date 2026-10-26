using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.DeactivateBarberProfile;

/// <summary>
/// Обработчик команды <see cref="DeactivateBarberProfileCommand"/>.
/// </summary>
public class DeactivateBarberProfileHandler : ICommandHandler<DeactivateBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DeactivateBarberProfileHandler> _logger;

    public DeactivateBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<DeactivateBarberProfileHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает корень профиля, деактивирует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        DeactivateBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetRootByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var allowed = _currentUser.EnsureCanManage(profile.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var deactivated = profile.Deactivate();
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber profile {BarberProfileId} has been deactivated.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
