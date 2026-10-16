using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.UnverifyBarberProfile;

/// <summary>
/// Обработчик команды <see cref="UnverifyBarberProfileCommand"/>.
/// </summary>
public class UnverifyBarberProfileHandler : ICommandHandler<UnverifyBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<UnverifyBarberProfileHandler> _logger;

    public UnverifyBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<UnverifyBarberProfileHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, снимает верификацию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UnverifyBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var unverified = profile.Unverify();
        if (unverified.IsFailure)
        {
            return unverified.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber profile {BarberProfileId} has been unverified.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
