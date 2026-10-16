using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.UpdateSalonContactInfo;

/// <summary>
/// Обработчик команды <see cref="UpdateSalonContactInfoCommand"/>.
/// </summary>
public class UpdateSalonContactInfoHandler : ICommandHandler<UpdateSalonContactInfoCommand>
{
    private readonly ISalonRepository _repository;
    private readonly ILogger<UpdateSalonContactInfoHandler> _logger;

    public UpdateSalonContactInfoHandler(
        ISalonRepository repository,
        ILogger<UpdateSalonContactInfoHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает салон, применяет к нему изменение и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateSalonContactInfoCommand command,
        CancellationToken cancellationToken)
    {
        var salonResult = await _repository.GetByIdAsync(command.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var updated = salon.UpdateContactInfo(
            command.PhoneNumber,
            command.WebsiteUrl);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(salon, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Salon {SalonId} contact info has been updated.", salon.Id);

        return UnitResult.Success<Failure>();
    }
}
