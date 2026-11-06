using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.UpdateSalonContactInfo;

/// <summary>
/// Обработчик команды <see cref="UpdateSalonContactInfoCommand"/>.
/// </summary>
/// <remarks>
/// Контакты правит администратор или владелец салона; чужой салон — <c>403 auth.forbidden</c>.
/// Модерацию, видимость и имя эта команда не меняет.
/// </remarks>
public class UpdateSalonContactInfoHandler : ICommandHandler<UpdateSalonContactInfoCommand>
{
    private readonly ISalonRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateSalonContactInfoHandler> _logger;

    public UpdateSalonContactInfoHandler(
        ISalonRepository repository,
        ICurrentUser currentUser,
        ILogger<UpdateSalonContactInfoHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает салон, проверяет право правки, применяет изменение и сохраняет агрегат.
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

        var allowed = _currentUser.EnsureCanManageSalon(salon);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

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
