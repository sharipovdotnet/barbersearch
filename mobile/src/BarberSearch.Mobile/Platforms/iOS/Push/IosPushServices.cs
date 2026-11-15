using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Заглушка разрешения на уведомления для iOS.
/// </summary>
/// <remarks>
/// Push на iOS — отдельный срез на APNs: в этом срезе FCM есть только на Android, и сборка
/// iOS не должна тянуть ни Firebase, ни новые разрешения. Заглушка отвечает «не выдано»,
/// поэтому «Профиль» не показывает строку напоминаний (канал всегда выключен), а регистратор
/// ничего не отправляет.
/// </remarks>
public sealed class IosPushPermission : IPushPermission
{
    /// <inheritdoc />
    public Task<bool> IsGrantedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> RequestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <inheritdoc />
    public void OpenSettings()
    {
    }
}

/// <summary>
/// Заглушка источника токена для iOS.
/// </summary>
/// <remarks>
/// Контракт <see cref="IPushTokenProvider"/> общий для платформ, но реальный токен в этом
/// срезе умеет только Android. Событие объявлено пустыми аксессорами: подписчику оно нужно
/// только для Android, а «никогда не поднимается» здесь — честное поведение.
/// </remarks>
public sealed class IosPushTokenProvider : IPushTokenProvider
{
    /// <inheritdoc />
    public event EventHandler<string>? TokenRefreshed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<PushTokenResult> GetTokenAsync(
        bool requestPermission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PushTokenResult.Disabled);
}
