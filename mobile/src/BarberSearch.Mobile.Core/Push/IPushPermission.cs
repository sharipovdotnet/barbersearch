namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Разрешение на уведомления: проверка, запрос и переход в настройки телефона.
/// </summary>
/// <remarks>
/// На Android 13+ без <c>POST_NOTIFICATIONS</c> уведомление не показывается, а системный
/// диалог даётся один раз: после отказа человек включает уведомления только в настройках,
/// поэтому интерфейс «Профиля» показывает подсказку и кнопку «Открыть настройки».
/// </remarks>
public interface IPushPermission
{
    /// <summary>
    /// Выдано ли разрешение сейчас.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если уведомления разрешены.</returns>
    Task<bool> IsGrantedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Просит разрешение, если оно ещё не выдано и система готова показать диалог.
    /// </summary>
    /// <remarks>
    /// Повторный вызов после отказа диалог не показывает: Android его уже не покажет,
    /// и приложение не должно выглядеть сломанным. Открытие настроек — отдельный метод.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если разрешение выдано.</returns>
    Task<bool> RequestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Открывает системные настройки приложения, где человека ждёт переключатель уведомлений.
    /// </summary>
    void OpenSettings();
}
