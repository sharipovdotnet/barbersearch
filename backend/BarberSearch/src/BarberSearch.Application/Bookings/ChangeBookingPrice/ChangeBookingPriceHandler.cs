using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.ChangeBookingPrice;

/// <summary>
/// Обработчик команды <see cref="ChangeBookingPriceCommand"/>.
/// </summary>
/// <remarks>
/// Порядок шагов важен: сначала «поле передано», потом существование брони, потом права,
/// и только затем доменное правило суммы. Иначе чужой клиент получал бы <c>400</c> вместо
/// <c>403</c>, а несуществующая бронь отвечала бы на отсутствующее поле.
/// Advisory-блокировка и <c>IUnitOfWork</c> не нужны: цена не пересекается по времени
/// с другими бронями, а гонку ловит <c>xmin</c> в <c>UpdateAsync</c>.
/// </remarks>
public class ChangeBookingPriceHandler : ICommandHandler<ChangeBookingPriceCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _barberProfiles;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ChangeBookingPriceHandler> _logger;

    public ChangeBookingPriceHandler(
        IBookingRepository repository,
        IBarberProfileRepository barberProfiles,
        ICurrentUser currentUser,
        ILogger<ChangeBookingPriceHandler> logger)
    {
        _repository = repository;
        _barberProfiles = barberProfiles;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает бронь, меняет цену визита и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ChangeBookingPriceCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Price is not { } price)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.PriceRequired,
                "A booking price is required.",
                nameof(command.Price)));
        }

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

        var priced = booking.ChangePrice(price);
        if (priced.IsFailure)
        {
            return priced.Error;
        }

        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Без PII: в журнал приложения тоже незачем тянуть сумму вместе с клиентом.
        _logger.LogInformation(
            "Booking {BookingId} price has been changed to {Price}.",
            booking.Id,
            price);

        return UnitResult.Success<Failure>();
    }
}
