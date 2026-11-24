using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.HairServices;
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
    private readonly IHairServiceRepository _hairServices;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfileServiceHandler> _logger;

    public AddBarberProfileServiceHandler(
        IBarberProfileRepository repository,
        IHairServiceRepository hairServices,
        IUnitOfWork unitOfWork,
        ILogger<AddBarberProfileServiceHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _hairServices = hairServices;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с услугами, добавляет услугу и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfileServiceCommand command,
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

        // Ссылка на справочник услуг проверяется до вставки, в той же транзакции.
        // Внешний ключ fk_barber_services_hair_services_hair_service_id — последняя линия.
        var result = await _unitOfWork.ExecuteAsync(async ct =>
        {
            var serviceExists = await _hairServices.ExistsAsync(command.HairServiceId, ct);
            if (serviceExists.IsFailure)
            {
                return serviceExists.Error;
            }

            if (!serviceExists.Value)
            {
                return new Failure(Error.NotFound(
                    "barberProfile.addService.hairServiceNotFound",
                    $"Hair service '{command.HairServiceId}' was not found.",
                    command.HairServiceId));
            }

            var added = profile.AddService(
                command.HairServiceId,
                command.Price,
                command.DurationMinutes);
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
            "Service {HairServiceId} has been added to barber profile {BarberProfileId}.",
            command.HairServiceId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
