using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberSalons.AddBarberSalonDayOff;

/// <summary>
/// Обработчик команды <see cref="AddBarberSalonDayOffCommand"/>.
/// </summary>
/// <remarks>
/// Салон ищется внутри загруженного агрегата — так выходной нельзя привязать к чужому салону.
/// </remarks>
public class AddBarberSalonDayOffHandler : ICommandHandler<AddBarberSalonDayOffCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<AddBarberSalonDayOffHandler> _logger;

    public AddBarberSalonDayOffHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberSalonDayOffHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Помечает день недели нерабочим и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberSalonDayOffCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var salonResult = profile.FindSalon(command.SalonId);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var added = salon.AddDayOff(command.DayOfWeek);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Day off {DayOfWeek} has been added to barber salon {SalonId}.",
            command.DayOfWeek,
            salon.Id);

        return UnitResult.Success<Failure>();
    }
}
