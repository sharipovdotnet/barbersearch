using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.UpdateBarberProfileService;

/// <summary>
/// Обработчик команды <see cref="UpdateBarberProfileServiceCommand"/>.
/// </summary>
public class UpdateBarberProfileServiceHandler
    : ICommandHandler<UpdateBarberProfileServiceCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateBarberProfileServiceHandler> _logger;

    public UpdateBarberProfileServiceHandler(
        IBarberProfileRepository repository,
        ILogger<UpdateBarberProfileServiceHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с услугами, меняет цену и длительность и сохраняет агрегат.
    /// </summary>
    /// <remarks>
    /// <c>EnsureCanManage</c> вызывается до доменной мутации: чужой профиль не меняется
    /// и не сохраняется, даже если запрос валиден.
    /// </remarks>
    public async Task<UnitResult<Failure>> Handle(
        UpdateBarberProfileServiceCommand command,
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

        var updated = profile.UpdateService(
            command.HairServiceId,
            command.Price,
            command.DurationMinutes);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Service {HairServiceId} of barber profile {BarberProfileId} has been updated.",
            command.HairServiceId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
