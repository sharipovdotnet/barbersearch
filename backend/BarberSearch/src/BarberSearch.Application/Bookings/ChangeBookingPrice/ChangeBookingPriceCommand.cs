using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.ChangeBookingPrice;

/// <summary>
/// Команда изменения цены визита.
/// </summary>
/// <remarks>
/// Цена nullable: пропущенное поле запроса не должно превратиться в <c>0</c>, потому что
/// ноль — валидная сумма, и визит молча подешевел бы до нуля (D5). <c>null</c> здесь
/// означает «поле не передано» и даёт <c>booking.price.required</c>.
/// </remarks>
/// <param name="BookingId">Идентификатор брони из маршрута.</param>
/// <param name="Price">Новая цена визита или <c>null</c>, если поле не передано.</param>
public record ChangeBookingPriceCommand(Guid BookingId, decimal? Price) : ICommand;
