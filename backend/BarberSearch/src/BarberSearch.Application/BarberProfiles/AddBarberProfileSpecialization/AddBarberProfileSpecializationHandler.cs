using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileSpecialization;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfileSpecializationCommand"/>.
/// </summary>
public class AddBarberProfileSpecializationHandler
    : ICommandHandler<AddBarberProfileSpecializationCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfileSpecializationHandler> _logger;

    public AddBarberProfileSpecializationHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberProfileSpecializationHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль барбера, добавляет специализацию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfileSpecializationCommand command,
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

        var added = profile.AddSpecialization(command.SpecializationId);
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
            "Specialization {SpecializationId} has been added to barber profile {BarberProfileId}.",
            command.SpecializationId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
