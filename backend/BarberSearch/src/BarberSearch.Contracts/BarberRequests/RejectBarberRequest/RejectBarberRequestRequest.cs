namespace BarberSearch.Contracts.BarberRequests.RejectBarberRequest;

/// <summary>
/// Данные запроса на отклонение заявки в барберы.
/// </summary>
/// <remarks>
/// Причина обязательна: заявитель должен увидеть, почему ему отказали. Поле объявлено
/// nullable намеренно — пустое значение должно получить публичный код
/// <c>barberRequest.rejection.reasonRequired</c> от домена, а не общий отказ model binding.
/// </remarks>
/// <param name="Reason">Причина отказа; до <c>500</c> символов.</param>
public record RejectBarberRequestRequest(string? Reason);
