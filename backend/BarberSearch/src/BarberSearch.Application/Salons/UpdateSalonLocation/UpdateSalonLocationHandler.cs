using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.UpdateSalonLocation;

/// <summary>
/// Обработчик команды <see cref="UpdateSalonLocationCommand"/>.
/// </summary>
public class UpdateSalonLocationHandler : ICommandHandler<UpdateSalonLocationCommand>
{
    private readonly ISalonRepository _repository;
    private readonly ILogger<UpdateSalonLocationHandler> _logger;

    public UpdateSalonLocationHandler(
        ISalonRepository repository,
        ILogger<UpdateSalonLocationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает салон, применяет к нему изменение и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateSalonLocationCommand command,
        CancellationToken cancellationToken)
    {
        var salonResult = await _repository.GetByIdAsync(command.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var updated = salon.UpdateLocation(
            command.Address,
            command.City,
            command.Country,
            command.TimeZoneId,
            command.Latitude,
            command.Longitude,
            command.PostalCode);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(salon, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Salon {SalonId} location has been updated.", salon.Id);

        return UnitResult.Success<Failure>();
    }
}
