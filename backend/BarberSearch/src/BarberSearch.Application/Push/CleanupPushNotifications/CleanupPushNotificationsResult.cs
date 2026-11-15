namespace BarberSearch.Application.Push.CleanupPushNotifications;

/// <summary>
/// Итог уборки push-данных.
/// </summary>
/// <remarks>
/// Две ретенции считаются отдельно: строка очереди живёт 30 дней (в ней только салон, мастер,
/// услуга и время), а токен устройства — 180 дней без повторной регистрации (это
/// идентификатор устройства, и чем меньше его хранить, тем лучше для 152-ФЗ).
/// </remarks>
/// <param name="DeletedNotifications">Сколько строк <c>push_notifications</c> удалено.</param>
/// <param name="DeletedDeviceTokens">Сколько строк <c>push_device_tokens</c> удалено.</param>
public sealed record CleanupPushNotificationsResult(int DeletedNotifications, int DeletedDeviceTokens);
