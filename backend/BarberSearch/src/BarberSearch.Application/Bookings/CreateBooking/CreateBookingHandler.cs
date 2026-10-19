using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.CreateBooking;

/// <summary>
/// Обработчик команды <see cref="CreateBookingCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Клиент брони берётся из токена (<see cref="ICurrentUser"/>), а не из тела запроса, поэтому
/// создать бронь от чужого имени нельзя; в рамках этого же пользователя ищется и ключ
/// идемпотентности. Первым шагом проверяется ключ: если бронь с таким ключом уже есть,
/// возвращается её идентификатор, и вторая бронь не создаётся. Отсутствие записи по ключу
/// (<see cref="ErrorType.NOT_FOUND"/>) — не ошибка сценария, а признак того, что создание
/// нужно продолжить.
/// </para>
/// <para>
/// Цена берётся из <c>BarberService.Price</c>, а не из запроса: клиент не должен назначать
/// стоимость сам. Услуга ищется узким чтением профиля и должна принадлежать именно этому
/// барберу и быть активной; иначе — <c>404</c>.
/// </para>
/// <para>
/// Гонку двух одновременных вставок разрешает PostgreSQL: exclusion-ограничение
/// <c>ex_bookings_no_overlap</c> отдаёт <c>23P01</c>, unique-индекс
/// <c>ux_bookings_user_id_idempotency_key</c> — <c>23505</c>. Оба маппятся в <c>Conflict</c>.
/// После такого отказа обработчик ещё раз читает бронь по ключу: если параллельный запрос
/// с тем же ключом уже сохранил её, возвращается его идентификатор (идемпотентный ответ),
/// иначе наружу уходит исходный <c>409</c>.
/// </para>
/// </remarks>
public class CreateBookingHandler : ICommandHandler<Guid, CreateBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _profiles;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository profiles,
        ICurrentUser currentUser,
        ILogger<CreateBookingHandler> logger)
    {
        _repository = repository;
        _profiles = profiles;
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

        // Ключ сравнивается и сохраняется в одном виде: иначе " key " и "key" были бы
        // разными ключами для unique-индекса, хотя клиент считает их одним запросом.
        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;

        var existing = await _repository.GetByIdempotencyKeyAsync(
            userId,
            idempotencyKey,
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

        var service = await _profiles.GetServiceAsync(
            request.BarberProfileId,
            request.BarberServiceId,
            cancellationToken);
        if (service.IsFailure)
        {
            return service.Error;
        }

        if (!service.Value.IsActive)
        {
            return new Failure(Error.NotFound(
                "booking.create.serviceInactive",
                $"""
                 Barber service '{request.BarberServiceId}' is inactive and cannot be booked.
                 """,
                request.BarberServiceId));
        }

        var booking = Booking.Create(
            userId,
            request.BarberProfileId,
            request.BarberServiceId,
            request.SalonId,
            request.StartAt,
            request.EndAt,
            service.Value.Price,
            idempotencyKey,
            request.CustomerNote);
        if (booking.IsFailure)
        {
            return booking.Error;
        }

        var added = await _repository.AddAsync(booking.Value, cancellationToken);
        if (added.IsFailure)
        {
            if (added.Error.Errors.Any(error => error.Type == ErrorType.CONFLICT))
            {
                // Параллельный запрос мог успеть раньше. Для одного ключа это не ошибка,
                // а повтор: читаем победившую бронь и возвращаем её id.
                var raced = await _repository.GetByIdempotencyKeyAsync(
                    userId,
                    idempotencyKey,
                    cancellationToken);
                if (raced.IsSuccess)
                {
                    _logger.LogInformation(
                        "Booking {BookingId} has been returned after a conflicting insert.",
                        raced.Value.Id);

                    return raced.Value.Id;
                }

                if (added.Error.Errors.Any(error => error.Code.EndsWith(
                        ".uniqueViolation",
                        StringComparison.Ordinal)))
                {
                    return new Failure(Error.Conflict(
                        "booking.create.idempotencyKeyConflict",
                        $"""
                         A booking with idempotency key '{idempotencyKey}' already exists
                         for this user.
                         """));
                }
            }

            return added.Error;
        }

        _logger.LogInformation("Booking {BookingId} has been created.", booking.Value.Id);

        return booking.Value.Id;
    }
}
