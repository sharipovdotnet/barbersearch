using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfileTimeOffCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Отпуск профиля-wide, поэтому проверяются только пересечения с активными визитами барбера:
/// пояса не нужны, интервал хранится в UTC.
/// </para>
/// <para>
/// Блокировка барбера и проверка визитов идут до <c>AddTimeOff</c> в одной транзакции:
/// между проверкой и сохранением не может вклиниться новая запись. При <c>force</c> брони
/// не читаются вовсе.
/// </para>
/// </remarks>
public class AddBarberProfileTimeOffHandler
    : ICommandHandler<ScheduleWriteOutcome, AddBarberProfileTimeOffCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AddBarberProfileTimeOffHandler> _logger;

    public AddBarberProfileTimeOffHandler(
        IBarberProfileRepository repository,
        IBookingRepository bookings,
        IUnitOfWork unitOfWork,
        ILogger<AddBarberProfileTimeOffHandler> logger,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _bookings = bookings;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Загружает профиль с отпусками, проверяет визиты, добавляет период и сохраняет агрегат.
    /// </summary>
    public Task<Result<ScheduleWriteOutcome, Failure>> Handle(
        AddBarberProfileTimeOffCommand command,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteAsync(
            token => AddTimeOffAsync(command, token),
            cancellationToken);

    private async Task<Result<ScheduleWriteOutcome, Failure>> AddTimeOffAsync(
        AddBarberProfileTimeOffCommand command,
        CancellationToken cancellationToken)
    {
        var lockResult = await _bookings.AcquireBarberLockAsync(
            command.BarberProfileId,
            ApiErrorCodes.Schedule.LockTimeout,
            cancellationToken);
        if (lockResult.IsFailure)
        {
            return lockResult.Error;
        }

        // Исключения и салоны здесь не нужны, но все пять пишущих ручек грузят один агрегат:
        // он уже содержит отпуска, и второе чтение ради них не требуется.
        var profileResult = await _repository.GetWithScheduleAsync(
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

        if (!command.Force)
        {
            var check = await ScheduleConflictCheck.FindInsideTimeOffAsync(
                _bookings,
                profile.Id,
                command.StartAt,
                command.EndAt,
                _timeProvider.GetUtcNow(),
                _logger,
                cancellationToken);
            if (check.IsFailure)
            {
                return check.Error;
            }

            if (check.Value.Conflicts.Count > 0)
            {
                return check.Value;
            }
        }

        var added = profile.AddTimeOff(command.StartAt, command.EndAt, command.Reason);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Time off has been added to barber profile {BarberProfileId}.", profile.Id);

        return ScheduleWriteOutcome.Saved;
    }
}
