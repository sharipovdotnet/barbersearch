namespace BarberSearch.Contracts.Bookings.ChangeBookingPrice;

/// <summary>
/// Данные запроса на изменение цены визита.
/// </summary>
/// <remarks>
/// <para>
/// Цена — часть брони, поэтому отдельного ресурса у неё нет: значение пишется в саму бронь,
/// а идентификатор брони приходит из маршрута. <c>PUT</c> выбран потому, что это замена
/// одного поля существующего ресурса, как у заметки барбера (<c>barber-note</c>);
/// <c>POST</c> в контроллере занят переходами статуса и переносом.
/// </para>
/// <para>
/// <see cref="Price"/> nullable намеренно: значение по умолчанию <c>0</c> — это валидная
/// цена, и пропущенное поле молча обнулило бы визит. Отсутствующее поле даёт
/// <c>400 booking.price.required</c>. Успех — <c>204</c> без тела.
/// </para>
/// </remarks>
/// <param name="Price">Новая цена визита; обязательна.</param>
public record ChangeBookingPriceRequest(decimal? Price);
