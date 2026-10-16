using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.UpdateBarberProfile;

/// <summary>
/// Обработчик команды <see cref="UpdateBarberProfileCommand"/>.
/// </summary>
public class UpdateBarberProfileHandler : ICommandHandler<UpdateBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<UpdateBarberProfileHandler> _logger;

    public UpdateBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<UpdateBarberProfileHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, применяет к нему изменение и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var request = command.Request;

        var updated = profile.UpdateProfile(
            displayName: request.DisplayName,
            yearsOfExperience: request.YearsOfExperience,
            bio: request.Bio,
            languages: request.Languages,
            certificatesAndTraining: request.CertificatesAndTraining,
            achievements: request.Achievements,
            paymentMethods: request.PaymentMethods,
            communicationFormat: request.CommunicationFormat,
            goodies: request.Goodies,
            latePolicy: request.LatePolicy);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber profile {BarberProfileId} has been updated.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
