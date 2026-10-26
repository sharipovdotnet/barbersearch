using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileSalon;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberProfileSalonCommand"/>.
/// </summary>
public class RemoveBarberProfileSalonHandler : ICommandHandler<RemoveBarberProfileSalonCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveBarberProfileSalonHandler> _logger;

    public RemoveBarberProfileSalonHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfileSalonHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с салонами, удаляет салон и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfileSalonCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithSalonsAsync(
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

        var removed = profile.RemoveSalon(command.SalonId);
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
            "Salon {SalonId} has been removed from barber profile {BarberProfileId}.",
            command.SalonId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
