using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Specializations;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.HairServices;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.HairServices.CreateHairService;

/// <summary>
/// Обработчик команды <see cref="CreateHairServiceCommand"/>.
/// </summary>
public class CreateHairServiceHandler : ICommandHandler<Guid, CreateHairServiceCommand>
{
    private readonly IHairServiceRepository _repository;
    private readonly ISpecializationRepository _specializations;
    private readonly ILogger<CreateHairServiceHandler> _logger;

    public CreateHairServiceHandler(
        IHairServiceRepository repository,
        ISpecializationRepository specializations,
        ILogger<CreateHairServiceHandler> logger)
    {
        _repository = repository;
        _specializations = specializations;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт услугу каталога и возвращает её идентификатор.
    /// </summary>
    /// <remarks>
    /// Домен проверяет форму (категория, длительность, непустое направление), а существование
    /// строки <c>specializations</c> — забота Application: это чужая граница агрегата.
    /// Внешний ключ <c>fk_hair_services_specializations_specialization_id</c> — последняя линия.
    /// </remarks>
    public async Task<Result<Guid, Failure>> Handle(
        CreateHairServiceCommand command,
        CancellationToken cancellationToken)
    {
        var hairService = HairService.Create(
            command.Name,
            command.Category,
            command.SpecializationId,
            command.DefaultDurationMinutes,
            command.Description);
        if (hairService.IsFailure)
        {
            return hairService.Error;
        }

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

        var added = await _repository.AddAsync(hairService.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        _logger.LogInformation("Hair service {HairServiceId} has been created.", hairService.Value.Id);

        return hairService.Value.Id;
    }
}
