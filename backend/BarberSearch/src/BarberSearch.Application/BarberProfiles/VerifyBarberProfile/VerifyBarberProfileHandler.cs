using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.VerifyBarberProfile;

/// <summary>
/// Обработчик команды <see cref="VerifyBarberProfileCommand"/>.
/// </summary>
public class VerifyBarberProfileHandler : ICommandHandler<VerifyBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<VerifyBarberProfileHandler> _logger;

    public VerifyBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<VerifyBarberProfileHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, подтверждает его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        VerifyBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var verified = profile.Verify();
        if (verified.IsFailure)
        {
            return verified.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber profile {BarberProfileId} has been verified.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
