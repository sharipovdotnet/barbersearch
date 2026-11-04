using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.GetBarberSchedule;

/// <summary>
/// Обработчик запроса <see cref="GetBarberScheduleQuery"/>.
/// </summary>
/// <remarks>
/// Профиль читается вместе с салонами, расписанием, исключениями и отпусками; названия
/// и часовые пояса салонов — одним отдельным чтением. Публичная карточка сюда не подходит:
/// она скрывает неподтверждённые профили, а график заполняют до верификации.
/// </remarks>
public class GetBarberScheduleHandler
    : IQueryHandler<BarberScheduleResponse, GetBarberScheduleQuery>
{
    private const int DEFAULT_WINDOW_DAYS = 90;

    private static readonly DayOfWeek[] WEEK_ORDER =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    private readonly IBarberProfileRepository _profiles;
    private readonly ISalonRepository _salons;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetBarberScheduleHandler> _logger;

    public GetBarberScheduleHandler(
        IBarberProfileRepository profiles,
        ISalonRepository salons,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<GetBarberScheduleHandler> logger)
    {
        _profiles = profiles;
        _salons = salons;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<BarberScheduleResponse, Failure>> Handle(
        GetBarberScheduleQuery query,
        CancellationToken cancellationToken)
    {
        var profileResult = await _profiles.GetWithScheduleAsync(
            query.BarberProfileId,
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

        var now = _timeProvider.GetUtcNow();
        var from = query.From ?? DateOnly.FromDateTime(now.UtcDateTime);
        var to = query.To ?? from.AddDays(DEFAULT_WINDOW_DAYS);

        if (to < from)
        {
            return new Failure(Error.Validation(
                "barberSchedule.range",
                "The end of the schedule window cannot be earlier than its start.",
                nameof(query.To)));
        }

        var salonIds = profile.Salons.Select(salon => salon.SalonId).Distinct().ToArray();
        var salonsResult = await _salons.GetByIdsAsync(salonIds, cancellationToken);
        if (salonsResult.IsFailure)
        {
            return salonsResult.Error;
        }

        var salonsById = salonsResult.Value.ToDictionary(salon => salon.Id);

        var salons = new List<BarberScheduleSalonResponse>(profile.Salons.Count);

        foreach (var barberSalon in profile.Salons
                     .OrderByDescending(salon => salon.IsPrimary)
                     .ThenBy(salon => salon.Id))
        {
            if (!salonsById.TryGetValue(barberSalon.SalonId, out var salon))
            {
                _logger.LogError(
                    "Barber salon {BarberSalonId} points to missing salon {SalonId}.",
                    barberSalon.Id,
                    barberSalon.SalonId);

                return new Failure(Error.Failure(
                    "barberSchedule.salonMissing",
                    $"Salon '{barberSalon.SalonId}' was not found."));
            }

            salons.Add(ToSalonResponse(barberSalon, salon, from, to));
        }

        var timeOffs = profile.TimeOffs
            .Where(timeOff => timeOff.EndAt >= now)
            .OrderBy(timeOff => timeOff.StartAt)
            .ThenBy(timeOff => timeOff.Id)
            .Select(timeOff => new BarberScheduleTimeOffResponse(
                timeOff.Id,
                timeOff.StartAt,
                timeOff.EndAt,
                timeOff.Reason))
            .ToList();

        return new BarberScheduleResponse(profile.Id, salons, timeOffs);
    }

    private static BarberScheduleSalonResponse ToSalonResponse(
        BarberSalon barberSalon,
        Salon salon,
        DateOnly from,
        DateOnly to)
    {
        var schedulesByDay = barberSalon.WorkingSchedules
            .ToDictionary(schedule => schedule.DayOfWeek);

        var weekly = WEEK_ORDER
            .Select(day =>
            {
                if (!schedulesByDay.TryGetValue(day, out var schedule))
                {
                    return new BarberScheduleDayResponse(
                        day,
                        true,
                        TimeOnly.MinValue,
                        TimeOnly.MinValue);
                }

                return new BarberScheduleDayResponse(
                    day,
                    schedule.IsDayOff,
                    schedule.StartTime,
                    schedule.EndTime);
            })
            .ToList();

        var exceptions = barberSalon.ScheduleExceptions
            .Where(exception => exception.Date >= from && exception.Date <= to)
            .OrderBy(exception => exception.Date)
            .Select(exception => new BarberScheduleExceptionResponse(
                exception.Date,
                exception.IsDayOff,
                exception.StartTime,
                exception.EndTime,
                exception.Reason))
            .ToList();

        return new BarberScheduleSalonResponse(
            salon.Id,
            salon.Name,
            salon.City,
            salon.TimeZoneId,
            barberSalon.IsPrimary,
            weekly,
            exceptions);
    }
}
