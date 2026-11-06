using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.UpdateSalonLocation;

/// <summary>
/// Обработчик команды <see cref="UpdateSalonLocationCommand"/>.
/// </summary>
/// <remarks>
/// Адрес и координаты правит администратор или владелец салона; чужой салон — <c>403
/// auth.forbidden</c>. Активацию, модерацию, имя и владельца эта команда не меняет.
/// </remarks>
public class UpdateSalonLocationHandler : ICommandHandler<UpdateSalonLocationCommand>
{
    private readonly ISalonRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateSalonLocationHandler> _logger;

    public UpdateSalonLocationHandler(
        ISalonRepository repository,
        ICurrentUser currentUser,
        ILogger<UpdateSalonLocationHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает салон, проверяет право правки, применяет изменение и сохраняет агрегат.
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

        var allowed = _currentUser.EnsureCanManageSalon(salon);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

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
