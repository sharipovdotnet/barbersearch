using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileService;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfileServiceCommand"/>.
/// </summary>
public class AddBarberProfileServiceHandler : ICommandHandler<AddBarberProfileServiceCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfileServiceHandler> _logger;

    public AddBarberProfileServiceHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberProfileServiceHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль барбера, добавляет услугу и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfileServiceCommand command,
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

        var added = profile.AddService(
            command.HairServiceId,
            command.Price,
            command.DurationMinutes);
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
            "Service {HairServiceId} has been added to barber profile {BarberProfileId}.",
            command.HairServiceId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
