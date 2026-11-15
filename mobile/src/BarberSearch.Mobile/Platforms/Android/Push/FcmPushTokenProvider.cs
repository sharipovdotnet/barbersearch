using BarberSearch.Mobile.Core.Push;
using Firebase.Messaging;
using GmsTask = Android.Gms.Tasks.Task;
using IOnCompleteListener = Android.Gms.Tasks.IOnCompleteListener;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Токен FCM с устройства.
/// </summary>
/// <remarks>
/// <para>
/// <c>FirebaseMessaging.Instance.GetToken()</c> возвращает не <c>Task&lt;string&gt;</c>, а
/// необщий <see cref="GmsTask"/>: результат приходит в <see cref="IOnCompleteListener"/>,
/// и превращать его в <see cref="Task{TResult}"/> приходится здесь. Версия
/// <c>Xamarin.Firebase.Messaging</c> закреплена в .csproj именно из-за этих API: 125.1+
/// объявляет их устаревшими.
/// </para>
/// <para>
/// Разрешение запрашивается только по просьбе регистратора: при обновлении токена в фоне
/// системный диалог показать нельзя, и отсутствие разрешения там означает «пропустить».
/// </para>
/// </remarks>
public sealed class FcmPushTokenProvider : IPushTokenProvider
{
    /// <summary>
    /// Сколько ждать ответа FCM; дольше держать экран «Профиля» бессмысленно.
    /// </summary>
    /// <remarks>
    /// Таймаут короткий намеренно: регистратор держит на это время семафор, а его ждёт
    /// в том числе выход из аккаунта — проверка токена не должна задерживать logout.
    /// </remarks>
    private static readonly TimeSpan TokenTimeout = TimeSpan.FromSeconds(10);

    private readonly IPushPermission _permission;

    /// <summary>
    /// Создаёт провайдер поверх разрешения на уведомления.
    /// </summary>
    /// <param name="permission">Проверка и запрос разрешения Android 13+.</param>
    public FcmPushTokenProvider(IPushPermission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        _permission = permission;
    }

    /// <inheritdoc />
    public event EventHandler<string>? TokenRefreshed;

    /// <inheritdoc />
    public async Task<PushTokenResult> GetTokenAsync(
        bool requestPermission,
        CancellationToken cancellationToken = default)
    {
        // Без FirebaseApp токен получить нечем: PushBootstrap уже решил, что канал выключен.
        if (!PushBootstrap.IsReady)
        {
            return PushTokenResult.Disabled;
        }

        if (!await _permission.IsGrantedAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!requestPermission
                || !await _permission.RequestAsync(cancellationToken).ConfigureAwait(false))
            {
                return PushTokenResult.Denied;
            }
        }

        try
        {
            var messaging = FirebaseMessaging.Instance;

            if (messaging is null)
            {
                return PushTokenResult.Unavailable;
            }

            // Автоинициализация включается явно: без неё обновления токена не приходят,
            // а именно на них держится перерегистрация устройства после переустановки.
            messaging.AutoInitEnabled = true;

            var completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Слушатель освобождается сам в OnComplete: `using` здесь освободил бы Java-объект
            // по таймауту ожидания, а FCM всё ещё мог бы вызвать его callback — это дало бы
            // ObjectDisposedException на ровном месте.
            var listener = new TokenCompleteListener(completion);

            messaging.GetToken().AddOnCompleteListener(listener);

            var token = await completion.Task
                .WaitAsync(TokenTimeout, cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(token)
                ? PushTokenResult.Unavailable
                : PushTokenResult.Available(token);
        }
        catch (Exception)
        {
            // Нет сервисов Google Play, нет сети, FCM ответил ошибкой — всё это «токена нет»,
            // а не повод ронять экран.
            return PushTokenResult.Unavailable;
        }
    }

    /// <summary>
    /// Сообщает о новом токене из <see cref="FcmMessagingService"/>.
    /// </summary>
    /// <remarks>
    /// Служба создаётся Android'ом, а не контейнером, поэтому получает этот экземпляр из DI
    /// и зовёт метод: событие остаётся единственной точкой, на которую подписан регистратор.
    /// </remarks>
    /// <param name="token">Новый токен FCM.</param>
    public void NotifyTokenRefreshed(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        TokenRefreshed?.Invoke(this, token);
    }

    /// <summary>
    /// Превращает результат <c>GetToken()</c> в <see cref="Task{TResult}"/>.
    /// </summary>
    /// <remarks>
    /// Освобождается в конце <see cref="OnComplete"/>: к этому моменту FCM уже не позовёт
    /// callback, а освобождение по таймауту ожидания оставляло бы висящий Java-объект,
    /// который система могла дёрнуть позже.
    /// </remarks>
    private sealed class TokenCompleteListener : Java.Lang.Object, IOnCompleteListener
    {
        private readonly TaskCompletionSource<string?> _completion;

        public TokenCompleteListener(TaskCompletionSource<string?> completion) =>
            _completion = completion;

        /// <inheritdoc />
        public void OnComplete(GmsTask task)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(task);

                _completion.TrySetResult(task.IsSuccessful ? task.Result?.ToString() : null);
            }
            finally
            {
                Dispose();
            }
        }
    }
}
