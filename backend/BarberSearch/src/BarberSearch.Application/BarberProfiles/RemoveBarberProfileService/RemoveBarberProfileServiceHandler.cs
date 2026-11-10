using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberProfileServiceCommand"/>.
/// </summary>
public class RemoveBarberProfileServiceHandler : ICommandHandler<RemoveBarberProfileServiceCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveBarberProfileServiceHandler> _logger;

    public RemoveBarberProfileServiceHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfileServiceHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с услугами, скрывает услугу (деактивация) и сохраняет агрегат.
    /// </summary>
    /// <remarks>
    /// Имя класса и команды осталось прежним — это имя ручки <c>DELETE .../services</c>, —
    /// но строка <c>barber_services</c> физически не удаляется: см.
    /// <c>BarberProfile.DeactivateService</c>. Повторный вызов на уже скрытой услуге успешен.
    /// </remarks>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfileServiceCommand command,
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

        var hidden = profile.DeactivateService(command.HairServiceId);
        if (hidden.IsFailure)
        {
            return hidden.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Service {HairServiceId} has been hidden from barber profile {BarberProfileId}.",
            command.HairServiceId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
