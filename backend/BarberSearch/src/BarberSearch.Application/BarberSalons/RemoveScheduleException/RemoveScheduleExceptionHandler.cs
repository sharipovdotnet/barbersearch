using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberSalons.RemoveScheduleException;

/// <summary>
/// Обработчик команды <see cref="RemoveScheduleExceptionCommand"/>.
/// </summary>
public class RemoveScheduleExceptionHandler
    : ICommandHandler<RemoveScheduleExceptionCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveScheduleExceptionHandler> _logger;

    public RemoveScheduleExceptionHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser,
        ILogger<RemoveScheduleExceptionHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<UnitResult<Failure>> Handle(
        RemoveScheduleExceptionCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithScheduleAsync(
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

        var salonResult = profile.FindSalon(command.SalonId);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var removed = salon.RemoveScheduleException(command.Date);
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
            "Schedule exception has been removed from barber salon {SalonId} on {Date}.",
            salon.Id,
            command.Date);

        return UnitResult.Success<Failure>();
    }
}
