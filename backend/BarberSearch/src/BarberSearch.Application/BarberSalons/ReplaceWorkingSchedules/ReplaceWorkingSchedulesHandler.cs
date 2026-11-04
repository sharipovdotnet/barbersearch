using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberSalons.ReplaceWorkingSchedules;

/// <summary>
/// Обработчик команды <see cref="ReplaceWorkingSchedulesCommand"/>.
/// </summary>
/// <remarks>
/// Салон ищется внутри загруженного агрегата — расписание нельзя привязать к чужому салону.
/// Замена идёт по дню недели: существующие строки обновляются, а не пересоздаются.
/// </remarks>
public class ReplaceWorkingSchedulesHandler
    : ICommandHandler<ReplaceWorkingSchedulesCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReplaceWorkingSchedulesHandler> _logger;

    public ReplaceWorkingSchedulesHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser,
        ILogger<ReplaceWorkingSchedulesHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<UnitResult<Failure>> Handle(
        ReplaceWorkingSchedulesCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithSalonsAndSchedulesAsync(
            command.BarberProfileId,
            cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var allowed = _currentUser.EnsureCanManage(profile.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var salonResult = profile.FindSalon(command.SalonId);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var replaced = salon.ReplaceWorkingSchedules(command.Days);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Weekly schedule has been replaced for barber salon {SalonId}.",
            salon.Id);

        return UnitResult.Success<Failure>();
    }
}
