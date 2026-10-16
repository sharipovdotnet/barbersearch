using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.CreateSalon;

/// <summary>
/// Обработчик команды <see cref="CreateSalonCommand"/>.
/// </summary>
public class CreateSalonHandler : ICommandHandler<Guid, CreateSalonCommand>
{
    private readonly ISalonRepository _repository;
    private readonly ILogger<CreateSalonHandler> _logger;

    public CreateSalonHandler(ISalonRepository repository, ILogger<CreateSalonHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт салон и возвращает его идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateSalonCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        var salon = Salon.Create(
            request.Name,
            request.Address,
            request.City,
            request.Country,
            request.TimeZoneId,
            request.Latitude,
            request.Longitude,
            request.PostalCode,
            request.PhoneNumber,
            request.WebsiteUrl);
        if (salon.IsFailure)
        {
            return salon.Error;
        }

        var added = await _repository.AddAsync(salon.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        _logger.LogInformation("Salon {SalonId} has been created.", salon.Value.Id);

        return salon.Value.Id;
    }
}
