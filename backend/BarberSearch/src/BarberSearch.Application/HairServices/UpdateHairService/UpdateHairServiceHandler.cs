using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Specializations;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.HairServices.UpdateHairService;

/// <summary>
/// Обработчик команды <see cref="UpdateHairServiceCommand"/>.
/// </summary>
public class UpdateHairServiceHandler : ICommandHandler<UpdateHairServiceCommand>
{
    private readonly IHairServiceRepository _repository;
    private readonly ISpecializationRepository _specializations;
    private readonly ILogger<UpdateHairServiceHandler> _logger;

    public UpdateHairServiceHandler(
        IHairServiceRepository repository,
        ISpecializationRepository specializations,
        ILogger<UpdateHairServiceHandler> logger)
    {
        _repository = repository;
        _specializations = specializations;
        _logger = logger;
    }

    /// <summary>
    /// Загружает услугу, применяет к ней изменение и сохраняет агрегат.
    /// </summary>
    /// <remarks>
    /// Смена направления проверяется до сохранения: новое направление обязано существовать,
    /// а ни один активный прайс не должен ссылаться на профиль без этой специализации.
    /// Иначе витрина показала бы услугу мастеру, который её не выполняет.
    /// </remarks>
    public async Task<UnitResult<Failure>> Handle(
        UpdateHairServiceCommand command,
        CancellationToken cancellationToken)
    {
        var hairServiceResult = await _repository.GetByIdAsync(command.HairServiceId, cancellationToken);
        if (hairServiceResult.IsFailure)
        {
            return hairServiceResult.Error;
        }

        var hairService = hairServiceResult.Value;

        if (command.SpecializationId != hairService.SpecializationId)
        {
            var specializationExists = await _specializations.ExistsAsync(
                command.SpecializationId,
                cancellationToken);
            if (specializationExists.IsFailure)
            {
                return specializationExists.Error;
            }

            if (!specializationExists.Value)
            {
                return new Failure(Error.NotFound(
                    ApiErrorCodes.HairService.SpecializationNotFound,
                    $"Specialization '{command.SpecializationId}' was not found.",
                    command.SpecializationId));
            }

            var withoutSpecialization = await _repository.CountActiveBarberServicesWithoutSpecializationAsync(
                hairService.Id,
                command.SpecializationId,
                cancellationToken);
            if (withoutSpecialization.IsFailure)
            {
                return withoutSpecialization.Error;
            }

            if (withoutSpecialization.Value > 0)
            {
                return new Failure(Error.Conflict(
                    ApiErrorCodes.HairService.UpdateSpecializationInUse,
                    $"""
                     The service is offered by {withoutSpecialization.Value} barber(s) without the
                     target specialization. Remove or reassign those price list rows first.
                     """));
            }
        }

        var updated = hairService.Update(
            command.Name,
            command.Description,
            command.DefaultDurationMinutes,
            command.SpecializationId);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(hairService, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Hair service {HairServiceId} has been updated.", hairService.Id);

        return UnitResult.Success<Failure>();
    }
}
