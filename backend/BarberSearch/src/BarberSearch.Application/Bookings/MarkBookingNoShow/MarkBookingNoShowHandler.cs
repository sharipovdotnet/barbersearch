using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.MarkBookingNoShow;

/// <summary>
/// Обработчик команды <see cref="MarkBookingNoShowCommand"/>.
/// </summary>
/// <remarks>
/// Правило «неявку нельзя отметить раньше начала брони» проверяется здесь, а не в домене:
/// сравнение с текущим временем — забота слоя Application (AGENT.md §7.6).
/// «Сейчас» берётся из <see cref="TimeProvider"/>, чтобы это правило проверялось в тестах
/// без ожидания и не зависело от системных часов.
/// </remarks>
public class MarkBookingNoShowHandler : ICommandHandler<MarkBookingNoShowCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _barberProfiles;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkBookingNoShowHandler> _logger;

    public MarkBookingNoShowHandler(
        IBookingRepository repository,
        IBarberProfileRepository barberProfiles,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<MarkBookingNoShowHandler> logger)
    {
        _repository = repository;
        _barberProfiles = barberProfiles;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Загружает бронь, проверяет, что её начало уже прошло, отмечает неявку и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        MarkBookingNoShowCommand command,
        CancellationToken cancellationToken)
    {
        var bookingResult = await _repository.GetByIdAsync(command.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;

        var allowed = await _currentUser.EnsureIsBarberOfAsync(
            _barberProfiles,
            booking.BarberProfileId,
            cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Правило уровня Application: раньше начала брони неявку отметить нельзя,
        // поэтому доменный метод в этом случае не вызывается вовсе.
        if (_timeProvider.GetUtcNow() < booking.StartAt)
        {
            return new Failure(Error.Validation(
                "booking.markNoShow.tooEarly",
                "A no-show cannot be reported before the booking start time."));
        }

        var marked = booking.MarkNoShow();
        if (marked.IsFailure)
        {
            return marked.Error;
        }

        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Booking {BookingId} has been marked as no-show.", booking.Id);

        return UnitResult.Success<Failure>();
    }
}
