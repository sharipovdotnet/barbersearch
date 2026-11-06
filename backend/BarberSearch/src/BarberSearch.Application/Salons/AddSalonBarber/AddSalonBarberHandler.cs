using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.AddSalonBarber;

/// <summary>
/// Обработчик команды <see cref="AddSalonBarberCommand"/>.
/// </summary>
/// <remarks>
/// Прикреплять барберов может владелец салона и администратор
/// (<c>EnsureCanManageSalon</c>); чужой салон — <c>403 auth.forbidden</c>.
/// Салон и профиль читаются в одной транзакции с записью: ссылка
/// <c>barber_salons</c> не может указывать на исчезнувший салон.
/// <para>
/// Первый салон профиля становится основным: слоты и форма записи выбирают
/// основной салон, и мастер без него не смог бы работать. Чужие салоны профиля
/// не меняются.
/// </para>
/// <para>
/// Профиль без подтверждения прикрепить можно — мастера добавляют до верификации.
/// Выключенный профиль — <c>409 salon.barbers.inactive</c>: он не участвует
/// в поиске, и привязка не дала бы ему работать.
/// </para>
/// </remarks>
public sealed class AddSalonBarberHandler : ICommandHandler<AddSalonBarberCommand>
{
    private readonly ISalonRepository _salons;
    private readonly IBarberProfileRepository _profiles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddSalonBarberHandler> _logger;

    public AddSalonBarberHandler(
        ISalonRepository salons,
        IBarberProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<AddSalonBarberHandler> logger)
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
        AddSalonBarberCommand command,
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

            if (!profile.IsActive)
            {
                return new Failure(Error.Conflict(
                    ApiErrorCodes.Salon.BarberInactive,
                    "An inactive barber profile cannot be attached to a salon."));
            }

            var linked = profile.AddSalon(salon.Id, isPrimary: profile.Salons.Count == 0);
            if (linked.IsFailure)
            {
                return linked.Error;
            }

            return await _profiles.UpdateAsync(profile, token);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        _logger.LogInformation(
            "Barber profile {BarberProfileId} has been attached to salon {SalonId}.",
            command.BarberProfileId,
            command.SalonId);

        return UnitResult.Success<Failure>();
    }
}
