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
    /// Порядок важен: домен проверяет форму (в том числе непустую специализацию) и только
    /// потом Application проверяет, что специализация существует. Иначе пустой
    /// идентификатор отвечал бы «не найдено» вместо ошибки валидации.
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

        var updated = hairService.Update(
            command.Name,
            command.Description,
            command.DefaultDurationMinutes,
            command.SpecializationId);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        // Смену специализации разрешено делать всегда, но новая строка обязана существовать.
        // До SaveChanges изменение живёт только в change tracker, поэтому отказ ничего не пишет.
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

        var saved = await _repository.UpdateAsync(hairService, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Hair service {HairServiceId} has been updated.", hairService.Id);

        return UnitResult.Success<Failure>();
    }
}
