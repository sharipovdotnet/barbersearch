using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Domain.BarberProfiles;
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
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateBarberProfileHandler> _logger;

    public UpdateBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<UpdateBarberProfileHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает корень профиля, применяет к нему изменение и сохраняет агрегат.
    /// </summary>
    /// <remarks>
    /// <see cref="BarberProfile.UpdateProfile"/> меняет только поля корня и его
    /// primitive-коллекции, поэтому дочерние коллекции не загружаются.
    /// </remarks>
    public async Task<UnitResult<Failure>> Handle(
        UpdateBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetRootByIdAsync(command.BarberProfileId, cancellationToken);
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
