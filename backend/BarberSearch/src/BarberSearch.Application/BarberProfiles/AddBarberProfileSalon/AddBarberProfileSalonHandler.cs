using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileSalon;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfileSalonCommand"/>.
/// </summary>
public class AddBarberProfileSalonHandler : ICommandHandler<AddBarberProfileSalonCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfileSalonHandler> _logger;

    public AddBarberProfileSalonHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberProfileSalonHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль барбера, добавляет салон и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfileSalonCommand command,
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

        var added = profile.AddSalon(command.SalonId, command.IsPrimary);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been added to barber profile {BarberProfileId}.",
            command.SalonId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
