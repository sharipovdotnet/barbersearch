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
/// Клиент брони берётся из токена (<see cref="ICurrentUser"/>), а не из тела запроса, поэтому
/// создать бронь от чужого имени нельзя; в рамках этого же пользователя ищется и ключ
/// идемпотентности. Первым шагом проверяется ключ: если бронь с таким ключом уже есть,
/// возвращается её идентификатор, и вторая бронь не создаётся. Отсутствие записи по ключу
/// (<see cref="ErrorType.NOT_FOUND"/>) — не ошибка сценария, а признак того, что создание
/// нужно продолжить.
/// </remarks>
public class CreateBookingHandler : ICommandHandler<Guid, CreateBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(
        IBookingRepository repository,
        ICurrentUser currentUser,
        ILogger<CreateBookingHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
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
        var userId = _currentUser.UserId;

        var existing = await _repository.GetByIdempotencyKeyAsync(
            userId,
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
            userId,
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
