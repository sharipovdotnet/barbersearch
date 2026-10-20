using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.ActivateBarberProfile;

/// <summary>
/// Обработчик команды <see cref="ActivateBarberProfileCommand"/>.
/// </summary>
public class ActivateBarberProfileHandler : ICommandHandler<ActivateBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ActivateBarberProfileHandler> _logger;

    public ActivateBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<ActivateBarberProfileHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль барбера, активирует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ActivateBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
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

        var activated = profile.Activate();
        if (activated.IsFailure)
        {
            return activated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber profile {BarberProfileId} has been activated.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
