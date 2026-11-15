namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Источник токена устройства: FCM на Android, заглушка на остальных платформах.
/// </summary>
/// <remarks>
/// Получение токена — единственное место, где нужны разрешение на уведомления и сервисы
/// Google Play, поэтому провайдер, а не регистратор, решает, какой из
/// <see cref="PushTokenStatus"/> вернуть. Разрешение запрашивается только по явной просьбе:
/// <c>requestPermission: false</c> используется при обновлении токена в фоне, где системный
/// диалог появиться не может.
/// </remarks>
public interface IPushTokenProvider
{
    /// <summary>
    /// FCM выдал новый токен устройства.
    /// </summary>
    /// <remarks>
    /// Событие приходит из <c>FirebaseMessagingService.OnNewToken</c> на фоновом потоке.
    /// Подписчик (регистратор) сам решает, есть ли сессия и разрешение, и не бросает
    /// исключений: служба Android не должна падать из-за сети.
    /// </remarks>
    event EventHandler<string>? TokenRefreshed;

    /// <summary>
    /// Возвращает текущий токен FCM.
    /// </summary>
    /// <param name="requestPermission">
    /// Запросить разрешение на уведомления, если его ещё нет (Android 13+). При <c>false</c>
    /// отсутствие разрешения сразу даёт <see cref="PushTokenStatus.Denied"/>.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Токен или причина, по которой его нет.</returns>
    Task<PushTokenResult> GetTokenAsync(
        bool requestPermission,
        CancellationToken cancellationToken = default);
}
