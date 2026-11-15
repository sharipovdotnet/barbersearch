namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Регистрация устройства на сервере и пользовательский переключатель напоминаний.
/// </summary>
/// <remarks>
/// Все методы — «не бросать и не блокировать экран»: регистрация и снятие с регистрации
/// побочны для входа и выхода, и сбой сети не должен ни мешать человеку, ни показывать ему
/// ошибку push, о которой он не просил.
/// </remarks>
public interface IPushRegistrar
{
    /// <summary>
    /// Настроен ли канал (есть действующий <c>push.json</c>).
    /// </summary>
    /// <remarks>
    /// По этому признаку «Профиль» скрывает строку «Присылать напоминания» целиком: без
    /// конфигурации Firebase переключатель обещал бы то, чего приложение не умеет.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>
    /// Включил ли человек напоминания (значение переключателя).
    /// </summary>
    bool IsUserEnabled { get; }

    /// <summary>
    /// Регистрирует устройство, если есть сессия, конфигурация и разрешение.
    /// </summary>
    /// <remarks>
    /// Первый вызов после входа — единственное место, где запрашивается разрешение на
    /// уведомления (Android 13+): до него приложение не показывает системный диалог.
    /// Повторный вызов с тем же токеном в той же сессии ничего не отправляет.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Состояние токена после попытки.</returns>
    Task<PushTokenResult> EnsureRegisteredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Снимает устройство с регистрации и стирает локальную метку токена.
    /// </summary>
    /// <remarks>
    /// Вызывается при явном выходе до <c>TokenSession.ClearAsync</c>: <c>POST unregister</c>
    /// идёт с Bearer, и после очистки пары запрос уже не авторизуется. Сбой не мешает выходу.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UnregisterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Регистрирует новый токен, выданный FCM.
    /// </summary>
    /// <param name="token">Новый токен устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task HandleTokenChangedAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Включает или выключает напоминания по выбору человека.
    /// </summary>
    /// <remarks>
    /// Включение сразу просит разрешение и регистрирует устройство; выключение снимает
    /// регистрацию и стирает метку, потому что сервер не должен слать напоминания на телефон,
    /// где их выключили.
    /// </remarks>
    /// <param name="enabled">Новое значение переключателя.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Состояние токена после попытки.</returns>
    Task<PushTokenResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Открывает настройки телефона, где включаются уведомления приложения.
    /// </summary>
    void OpenSettings();
}
