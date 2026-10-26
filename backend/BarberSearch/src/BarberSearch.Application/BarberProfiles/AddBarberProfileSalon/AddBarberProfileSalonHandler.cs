using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileSalon;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfileSalonCommand"/>.
/// </summary>
public class AddBarberProfileSalonHandler : ICommandHandler<AddBarberProfileSalonCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfileSalonHandler> _logger;

    public AddBarberProfileSalonHandler(
        IBarberProfileRepository repository,
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ILogger<AddBarberProfileSalonHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _salons = salons;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с салонами, добавляет салон и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfileSalonCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithSalonsAsync(
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

        // Проверка ссылки и сохранение дочерней сущности — в одной транзакции:
        // салон не может исчезнуть между EXISTS и INSERT. Тот же инвариант страхует
        // внешний ключ fk_barber_salons_salons_salon_id (R-02).
        var result = await _unitOfWork.ExecuteAsync(async ct =>
        {
            var salonExists = await _salons.ExistsAsync(command.SalonId, ct);
            if (salonExists.IsFailure)
            {
                return salonExists.Error;
            }

            if (!salonExists.Value)
            {
                return new Failure(Error.NotFound(
                    "barberProfile.addSalon.salonNotFound",
                    $"Salon '{command.SalonId}' was not found.",
                    command.SalonId));
            }

            var added = profile.AddSalon(command.SalonId, command.IsPrimary);
            if (added.IsFailure)
            {
                return added.Error;
            }

            return await _repository.UpdateAsync(profile, ct);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been added to barber profile {BarberProfileId}.",
            command.SalonId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
