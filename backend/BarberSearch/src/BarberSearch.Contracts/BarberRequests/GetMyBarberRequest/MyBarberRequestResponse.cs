namespace BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;

/// <summary>
/// Ответ на чтение своей заявки в барберы.
/// </summary>
/// <remarks>
/// Заявки может не быть: заявитель открыл форму впервые либо его прежняя заявка не сохранилась.
/// Тогда <see cref="Request"/> = <c>null</c>, а не <c>404</c> — отсутствие заявки не ошибка,
/// это штатное состояние формы «Стать барбером».
/// </remarks>
/// <param name="Request">Последняя заявка пользователя либо <c>null</c>.</param>
public record MyBarberRequestResponse(BarberRequestResponse? Request);
