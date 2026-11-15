namespace BarberSearch.Contracts.Push.UnregisterPushDevice;

/// <summary>
/// Данные запроса на снятие устройства с push-уведомлений.
/// </summary>
/// <remarks>
/// Отдельный <c>POST</c>, а не <c>DELETE api/push/devices/{token}</c>: токен — секрет устройства
/// и длинная строка, которой не место в URL и в логах прокси; тело запроса туда не попадает.
/// Неизвестный или чужой токен — успех без эффекта: ответ не должен подсказывать, зарегистрирован
/// ли токен у другого аккаунта.
/// </remarks>
/// <param name="Token">Токен устройства, снимаемого с регистрации; пустой недопустим.</param>
public record UnregisterPushDeviceRequest(string Token);
