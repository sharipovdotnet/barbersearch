using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberProfileServiceCommand"/>.
/// </summary>
public class RemoveBarberProfileServiceHandler : ICommandHandler<RemoveBarberProfileServiceCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveBarberProfileServiceHandler> _logger;

    public RemoveBarberProfileServiceHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfileServiceHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с услугами, удаляет услугу и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfileServiceCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithServicesAsync(
            command.BarberProfileId,
            cancellationToken);
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

        var removed = profile.RemoveService(command.HairServiceId);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Service {HairServiceId} has been removed from barber profile {BarberProfileId}.",
            command.HairServiceId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
