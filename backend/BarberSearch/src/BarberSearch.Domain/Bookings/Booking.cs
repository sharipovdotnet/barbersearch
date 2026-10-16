using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Bookings;

/// <summary>
/// Бронь — агрегатный корень. Хранит только идентификаторы барбера, салона и услуги:
/// все они — границы других агрегатов.
/// </summary>
public class Booking : AggregateRoot
{
    public Guid UserId { get; private set; }
    public Guid BarberProfileId { get; private set; }
    public Guid BarberServiceId { get; private set; }

    /// <summary>
    /// Салон, в котором оказана услуга. Нужен, потому что расписание — принадлежность
    /// пары (барбер, салон).
    /// </summary>
    public Guid SalonId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }

    public decimal Price { get; private set; }

    public BookingStatus Status { get; private set; }

    /// <summary>
    /// Ключ идемпотентности запроса на создание брони: мобильный клиент при обрыве сети
    /// повторяет запрос, и без ключа повтор создаст вторую бронь.
    /// </summary>
    public string IdempotencyKey { get; private set; } = null!;

    public string? CustomerNote { get; private set; }
    public string? BarberNote { get; private set; }

    private Booking()
    {
    }

    public static Result<Booking, Failure> Create(
        Guid userId,
        Guid barberProfileId,
        Guid barberServiceId,
        Guid salonId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal price,
        string idempotencyKey,
        string? customerNote = null)
    {
        var errors = DomainErrors.Collect();

        if (endAt <= startAt)
        {
            errors.Add(Error.Validation(
                "booking.create.period",
                "End time must be after start time.",
                nameof(endAt)));
        }

        if (price < 0)
        {
            errors.Add(Error.Validation(
                "booking.create.price",
                "Price cannot be negative.",
                nameof(price)));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            errors.Add(Error.Validation(
                "booking.create.idempotencyKey",
                "Idempotency key cannot be empty.",
                nameof(idempotencyKey)));
        }
        else if (idempotencyKey.Length > Constants.MAX_IDEMPOTENCY_KEY_LENGTH)
        {
            errors.Add(Error.Validation(
                "booking.create.idempotencyKey.tooLong",
                $"""
                 Idempotency key cannot be longer than 
                 {Constants.MAX_IDEMPOTENCY_KEY_LENGTH} characters.
                 """,
                nameof(idempotencyKey)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new Booking
        {
            UserId = userId,
            BarberProfileId = barberProfileId,
            BarberServiceId = barberServiceId,
            SalonId = salonId,
            StartAt = startAt,
            EndAt = endAt,
            Price = price,
            IdempotencyKey = idempotencyKey,
            CustomerNote = customerNote,
            Status = BookingStatus.Pending
        };
    }

    public UnitResult<Failure> Confirm()
    {
        if (Status != BookingStatus.Pending)
        {
            return InvalidStatus("booking.confirm.invalidStatus", BookingStatus.Confirmed);
        }

        Status = BookingStatus.Confirmed;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отмена. Недоступна для уже завершённой и уже отменённой брони.
    /// </summary>
    public UnitResult<Failure> Cancel()
    {
        if (Status is BookingStatus.Completed or BookingStatus.Cancelled)
        {
            return InvalidStatus("booking.cancel.invalidStatus", BookingStatus.Cancelled);
        }

        Status = BookingStatus.Cancelled;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Complete()
    {
        if (Status != BookingStatus.Confirmed)
        {
            return InvalidStatus("booking.complete.invalidStatus", BookingStatus.Completed);
        }

        Status = BookingStatus.Completed;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Клиент не пришёл. Возможно только из подтверждённой брони; правило
    /// «не раньше <see cref="StartAt"/>» проверяется в Application.
    /// </summary>
    public UnitResult<Failure> MarkNoShow()
    {
        if (Status != BookingStatus.Confirmed)
        {
            return InvalidStatus("booking.markNoShow.invalidStatus", BookingStatus.NoShow);
        }

        Status = BookingStatus.NoShow;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> AddBarberNote(string note)
    {
        BarberNote = note;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    private UnitResult<Failure> InvalidStatus(string code, BookingStatus target)
        => new Failure(Error.Conflict(
            code,
            $"Booking in status {Status} cannot be moved to {target}."));
}
