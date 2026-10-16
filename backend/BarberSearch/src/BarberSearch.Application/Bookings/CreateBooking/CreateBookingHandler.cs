using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.CreateBooking;

/// <summary>
/// Обработчик команды <see cref="CreateBookingCommand"/>.
/// </summary>
/// <remarks>
/// Первым шагом проверяется ключ идемпотентности: если бронь с таким ключом у пользователя
/// уже есть, возвращается её идентификатор, и вторая бронь не создаётся. Отсутствие записи
/// по ключу (<see cref="ErrorType.NOT_FOUND"/>) — не ошибка сценария, а признак того, что
/// создание нужно продолжить.
/// </remarks>
public class CreateBookingHandler : ICommandHandler<Guid, CreateBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(IBookingRepository repository, ILogger<CreateBookingHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт бронь и возвращает её идентификатор; повтор запроса с тем же ключом
    /// идемпотентности возвращает идентификатор уже существующей брони.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateBookingCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        var existing = await _repository.GetByIdempotencyKeyAsync(
            request.UserId,
            request.IdempotencyKey,
            cancellationToken);
        if (existing.IsSuccess)
        {
            _logger.LogInformation(
                "Booking {BookingId} has been returned by idempotency key.",
                existing.Value.Id);

            return existing.Value.Id;
        }

        var missing = existing.Error.Errors.Any(error => error.Type == ErrorType.NOT_FOUND);
        if (!missing)
        {
            return existing.Error;
        }

        var booking = Booking.Create(
            request.UserId,
            request.BarberProfileId,
            request.BarberServiceId,
            request.SalonId,
            request.StartAt,
            request.EndAt,
            request.Price,
            request.IdempotencyKey,
            request.CustomerNote);
        if (booking.IsFailure)
        {
            return booking.Error;
        }

        var added = await _repository.AddAsync(booking.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        _logger.LogInformation("Booking {BookingId} has been created.", booking.Value.Id);

        return booking.Value.Id;
    }
}
