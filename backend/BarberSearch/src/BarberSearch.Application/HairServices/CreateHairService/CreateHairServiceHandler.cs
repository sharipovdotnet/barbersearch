using BarberSearch.Application.Abstractions;
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
    private readonly ILogger<CreateHairServiceHandler> _logger;

    public CreateHairServiceHandler(
        IHairServiceRepository repository,
        ILogger<CreateHairServiceHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт услугу каталога и возвращает её идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateHairServiceCommand command,
        CancellationToken cancellationToken)
    {
        var hairService = HairService.Create(
            command.Name,
            command.Category,
            command.DefaultDurationMinutes,
            command.Description);
        if (hairService.IsFailure)
        {
            return hairService.Error;
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
