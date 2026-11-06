using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.RemoveSalonBarber;

/// <summary>
/// Обработчик команды <see cref="RemoveSalonBarberCommand"/>.
/// </summary>
/// <remarks>
/// Откреплять барберов может владелец салона и администратор
/// (<c>EnsureCanManageSalon</c>); чужой салон — <c>403 auth.forbidden</c>.
/// Владелец салона неприкосновенен: <c>409 salon.barbers.owner</c>, иначе салон
/// остался бы без мастера, создавшего его. Отсутствующая привязка — доменный
/// <c>404 barberProfile.removeSalon.notFound</c>.
/// </remarks>
public sealed class RemoveSalonBarberHandler : ICommandHandler<RemoveSalonBarberCommand>
{
    private readonly ISalonRepository _salons;
    private readonly IBarberProfileRepository _profiles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveSalonBarberHandler> _logger;

    public RemoveSalonBarberHandler(
        ISalonRepository salons,
        IBarberProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<RemoveSalonBarberHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(salons);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        _salons = salons;
        _profiles = profiles;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<UnitResult<Failure>> Handle(
        RemoveSalonBarberCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var salonResult = await _salons.GetByIdAsync(command.SalonId, token);
            if (salonResult.IsFailure)
            {
                return salonResult.Error;
            }

            var salon = salonResult.Value;

            var allowed = _currentUser.EnsureCanManageSalon(salon);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }

            var profileResult = await _profiles.GetWithSalonsAsync(
                command.BarberProfileId,
                token);
            if (profileResult.IsFailure)
            {
                return profileResult.Error;
            }

            var profile = profileResult.Value;

            if (salon.OwnerUserId is { } ownerUserId && profile.UserId == ownerUserId)
            {
                return new Failure(Error.Conflict(
                    ApiErrorCodes.Salon.BarberOwnerImmutable,
                    "The salon owner cannot be removed from the salon."));
            }

            var removed = profile.RemoveSalon(salon.Id);
            if (removed.IsFailure)
            {
                return removed.Error;
            }

            return await _profiles.UpdateAsync(profile, token);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        _logger.LogInformation(
            "Barber profile {BarberProfileId} has been detached from salon {SalonId}.",
            command.BarberProfileId,
            command.SalonId);

        return UnitResult.Success<Failure>();
    }
}
