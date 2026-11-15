using BarberSearch.Domain.Push;

namespace BarberSearch.Mobile.Core.Api.Push;

/// <summary>
/// Регистрация устройства для push-уведомлений: постановка и снятие токена FCM.
/// </summary>
/// <remarks>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: сервер берёт пользователя из
/// access-токена, поэтому тело содержит только токен устройства и платформу. Снятие —
/// отдельный <c>POST</c> с токеном в теле, а не <c>DELETE</c> с токеном в пути: токен —
/// секрет устройства, и ему не место в URL и в журналах прокси.
/// </remarks>
public interface IPushApi
{
    /// <summary>
    /// Регистрирует токен устройства за текущим пользователем.
    /// </summary>
    /// <remarks>
    /// Регистрация идемпотентна: повторный запрос с тем же токеном обновляет время последней
    /// активности, а токен, зарегистрированный другим аккаунтом на этом телефоне, переходит
    /// к текущему — иначе напоминание уходило бы прежнему владельцу.
    /// </remarks>
    /// <param name="token">Токен FCM.</param>
    /// <param name="platform">Платформа устройства; в этом срезе — Android.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task RegisterDeviceAsync(
        string token,
        PushPlatform platform,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Снимает токен устройства с регистрации.
    /// </summary>
    /// <remarks>
    /// Неизвестный или чужой токен — успех без эффекта: ответ не подсказывает, был ли токен
    /// зарегистрирован за другим аккаунтом.
    /// </remarks>
    /// <param name="token">Токен FCM.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UnregisterDeviceAsync(string token, CancellationToken cancellationToken = default);
}
