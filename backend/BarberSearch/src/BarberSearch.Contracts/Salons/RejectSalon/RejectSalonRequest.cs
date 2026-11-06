namespace BarberSearch.Contracts.Salons.RejectSalon;

/// <summary>
/// Данные запроса на отказ салону в модерации.
/// </summary>
/// <remarks>
/// Причина обязательна: отказ без объяснения барбер не сможет исправить. Валидацию
/// (не пустая после <c>Trim</c>, не длиннее 500 символов) выполняет домен, коды —
/// <c>salon.reject.reasonRequired</c> и <c>salon.reject.reasonTooLong</c>.
/// </remarks>
/// <param name="Reason">Причина отказа.</param>
public record RejectSalonRequest(string? Reason);
