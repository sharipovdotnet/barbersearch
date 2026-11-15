using BarberSearch.Domain.Push;
using BarberSearch.Mobile.Core.Api.Push;
using BarberSearch.Mobile.Core.Auth;

namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Регистратор устройства: связывает сессию, токен FCM, разрешение и API.
/// </summary>
/// <remarks>
/// <para>
/// Одна точка, где решается, надо ли регистрировать телефон: канал настроен, человек не
/// выключил напоминания, сессия есть и разрешение выдано. Поэтому вызывающие (старт
/// приложения, вход, обновление токена, переключатель в «Профиле») не повторяют эту
/// проверку и не показывают ошибок push.
/// </para>
/// <para>
/// Сессия — источник правды о входе, поэтому регистратор подписан на
/// <see cref="TokenSession.SessionStarted"/> и <see cref="TokenSession.SessionEnded"/>:
/// новая пара регистрирует токен за новым пользователем (сервер переводит токен к текущему
/// аккаунту), а завершённая запрещает запоздавшей регистрации записать метку. Снятие с
/// регистрации требует Bearer, поэтому при истёкшей сессии оно невозможно, и метка токена
/// остаётся до следующего входа.
/// </para>
/// <para>
/// Каждый публичный метод ловит всё: сбой push — не ошибка входа, выхода или экрана.
/// Временный сбой регистрации (сеть, <c>401/5xx</c>, таймаут) возвращается отдельным
/// состоянием, чтобы «Профиль» не показывал подсказку про настройки телефона.
/// </para>
/// </remarks>
public sealed class PushRegistrar : IPushRegistrar, IDisposable
{
    private readonly IPushApi _api;
    private readonly IPushTokenProvider _tokenProvider;
    private readonly IPushPermission _permission;
    private readonly IPushStateStore _state;
    private readonly TokenSession _session;
    private readonly PushOptions _options;

    /// <summary>
    /// Сериализует получение токена, регистрацию и снятие.
    /// </summary>
    /// <remarks>
    /// Семафор охватывает и запрос токена, и HTTP-вызов снятия: вход и появление «Профиля»
    /// могут прийти одновременно, два параллельных запроса разрешения показали бы два
    /// системных диалога, а снятие не должно пересечься с регистрацией того же токена.
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Токен, зарегистрированный в этой сессии; <c>null</c> — ещё не регистрировали.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="IPushStateStore.RegisteredToken"/>: метка в настройках переживает
    /// перезапуск, а здесь важно именно «в этой сессии уже отправлено», чтобы повторный
    /// вызов не делал второй <c>POST</c>.
    /// </remarks>
    private string? _registeredToken;

    /// <summary>
    /// Поколение сессии: растёт при входе, выходе и явном снятии устройства.
    /// </summary>
    /// <remarks>
    /// Проверяется под семафором до и после HTTP-вызова. Без него запоздавшая регистрация
    /// прежней сессии записала бы метку уже после выхода: новая сессия сочла бы токен
    /// зарегистрированным и не отправила свой <c>POST</c>, а сервер оставил бы токен за
    /// прежним пользователем.
    /// </remarks>
    private int _generation;

    private bool _disposed;

    /// <summary>
    /// Создаёт регистратор и подписывается на сессию и обновления токена.
    /// </summary>
    /// <param name="api">Клиент регистрации устройств.</param>
    /// <param name="tokenProvider">Источник токена FCM.</param>
    /// <param name="permission">Разрешение на уведомления.</param>
    /// <param name="state">Локальное состояние push.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    /// <param name="options">Настройки канала из <c>push.json</c>.</param>
    public PushRegistrar(
        IPushApi api,
        IPushTokenProvider tokenProvider,
        IPushPermission permission,
        IPushStateStore state,
        TokenSession session,
        PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);

        _api = api;
        _tokenProvider = tokenProvider;
        _permission = permission;
        _state = state;
        _session = session;
        _options = options;

        _session.SessionStarted += OnSessionStarted;
        _session.SessionEnded += OnSessionEnded;
        _tokenProvider.TokenRefreshed += OnTokenRefreshed;
    }

    /// <inheritdoc />
    public bool IsConfigured => _options.Enabled;

    /// <inheritdoc />
    public bool IsUserEnabled => _state.IsEnabled;

    /// <inheritdoc />
    public async Task<PushTokenResult> EnsureRegisteredAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Поколение снимается до проверки сессии: пока этот вызов ждёт семафор, вход или
            // выход могли уже произойти, и тогда регистрация относится к прошлой сессии.
            var generation = Volatile.Read(ref _generation);

            if (!_options.Enabled)
            {
                return PushTokenResult.Disabled;
            }

            if (!await _session.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                return PushTokenResult.Disabled;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (generation != Volatile.Read(ref _generation))
                {
                    return PushTokenResult.Disabled;
                }

                if (!_state.IsEnabled)
                {
                    // Человек выключил напоминания: если метка осталась от прошлой регистрации
                    // или от неудавшегося снятия, пробуем снять устройство снова — это и есть
                    // повтор, которого не было, пока метка стиралась до запроса.
                    await ClearRegistrationUnderGateAsync(cancellationToken).ConfigureAwait(false);

                    return PushTokenResult.Disabled;
                }

                var result = await _tokenProvider
                    .GetTokenAsync(requestPermission: true, cancellationToken)
                    .ConfigureAwait(false);

                if (!result.IsAvailable)
                {
                    return result;
                }

                // Пока ждали разрешение и токен, сессия могла закончиться: регистрировать
                // токен за уже вышедшим пользователем нельзя.
                if (generation != Volatile.Read(ref _generation))
                {
                    return PushTokenResult.Disabled;
                }

                return await RegisterUnderGateAsync(result.Token!, generation, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Экран закрыли или приложение уходит в фон — это не повод что-то менять.
            return PushTokenResult.TransientFailure;
        }
        catch (Exception)
        {
            return PushTokenResult.TransientFailure;
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Новое поколение: регистрация, начатая до снятия, не должна записать метку
                // после него.
                Interlocked.Increment(ref _generation);

                await ClearRegistrationUnderGateAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception)
        {
            // Выход не должен зависеть от push: метка уже либо снята, либо сохранена.
        }
    }

    /// <inheritdoc />
    public async Task HandleTokenChangedAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var generation = Volatile.Read(ref _generation);

            if (string.IsNullOrWhiteSpace(token) || !_options.Enabled || !_state.IsEnabled)
            {
                return;
            }

            if (!await _session.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                // Токен обновится до следующего входа — регистрация произойдёт тогда.
                return;
            }

            // Разрешение в фоне не запрашивается: системный диалог из службы FCM показать
            // нельзя, а его отсутствие означает, что напоминания человеку и не нужны.
            if (!await _permission.IsGrantedAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (generation != Volatile.Read(ref _generation))
                {
                    return;
                }

                await RegisterUnderGateAsync(token, generation, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception)
        {
            // Служба Android не должна падать из-за сети или хранилища.
        }
    }

    /// <inheritdoc />
    public async Task<PushTokenResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _state.SetEnabled(enabled);

            if (!_options.Enabled)
            {
                return PushTokenResult.Disabled;
            }

            if (!enabled)
            {
                await UnregisterAsync(cancellationToken).ConfigureAwait(false);

                return PushTokenResult.Disabled;
            }

            return await EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return PushTokenResult.TransientFailure;
        }
    }

    /// <inheritdoc />
    public void OpenSettings()
    {
        try
        {
            _permission.OpenSettings();
        }
        catch (Exception)
        {
            // Настройки телефона — не критичная часть: отсутствие экрана не ломает приложение.
        }
    }

    /// <summary>
    /// Отписывается от сессии и токена и освобождает семафор.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _session.SessionStarted -= OnSessionStarted;
        _session.SessionEnded -= OnSessionEnded;
        _tokenProvider.TokenRefreshed -= OnTokenRefreshed;
        _gate.Dispose();
    }

    /// <summary>
    /// Отправляет токен на сервер, соблюдая «один POST на сессию и токен».
    /// </summary>
    /// <remarks>
    /// Вызывается только с захваченным <see cref="_gate"/>. Успех записывается в метку
    /// исключительно после <c>204</c> и только если поколение сессии не изменилось: иначе
    /// новая сессия считала бы токен уже зарегистрированным и пропустила бы свой <c>POST</c>.
    /// Ошибка стирает метку — регистрация повторится, искать её в настройках не нужно.
    /// </remarks>
    private async Task<PushTokenResult> RegisterUnderGateAsync(
        string token,
        int generation,
        CancellationToken cancellationToken)
    {
        if (string.Equals(_registeredToken, token, StringComparison.Ordinal)
            && generation == Volatile.Read(ref _generation))
        {
            return PushTokenResult.Available(token);
        }

        try
        {
            await _api
                .RegisterDeviceAsync(token, PushPlatform.Android, cancellationToken)
                .ConfigureAwait(false);

            if (generation != Volatile.Read(ref _generation))
            {
                return PushTokenResult.Disabled;
            }

            _registeredToken = token;
            _state.SetRegisteredToken(token);

            return PushTokenResult.Available(token);
        }
        catch (Exception)
        {
            _registeredToken = null;
            _state.SetRegisteredToken(null);

            return PushTokenResult.TransientFailure;
        }
    }

    /// <summary>
    /// Снимает устройство с регистрации, сохраняя метку до успешного ответа сервера.
    /// </summary>
    /// <remarks>
    /// Вызывается только с захваченным <see cref="_gate"/>. Если запрос не прошёл, метка
    /// остаётся: следующий <see cref="EnsureRegisteredAsync"/> при выключенном переключателе
    /// или следующий вход смогут повторить снятие. Стереть метку заранее — значит потерять
    /// единственное, что известно о зарегистрированном токене.
    /// </remarks>
    private async Task ClearRegistrationUnderGateAsync(CancellationToken cancellationToken)
    {
        var token = _registeredToken ?? _state.RegisteredToken;

        _registeredToken = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            _state.SetRegisteredToken(null);

            return;
        }

        try
        {
            await _api.UnregisterDeviceAsync(token, cancellationToken).ConfigureAwait(false);

            _state.SetRegisteredToken(null);
        }
        catch (Exception)
        {
            _state.SetRegisteredToken(token);
        }
    }

    /// <summary>
    /// Новая пара токенов: регистрируем устройство за вошедшим пользователем.
    /// </summary>
    /// <remarks>
    /// Событие приходит с сетевого потока; регистратору UI не нужен, а разрешение запрашивает
    /// платформенный <see cref="IPushPermission"/>, который сам переходит на главный поток.
    /// </remarks>
    private void OnSessionStarted(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _generation);
        _registeredToken = null;

        _ = EnsureRegisteredAsync();
    }

    /// <summary>
    /// Сессия завершена: снять устройство без Bearer нельзя, поэтому метка остаётся.
    /// </summary>
    /// <remarks>
    /// Меняется только поколение и память: запоздавшая регистрация прежней сессии уже не
    /// запишет метку, а сохранённая метка позволит повторить снятие после следующего входа
    /// (когда переключатель выключен) или будет перезаписана регистрацией того же токена.
    /// </remarks>
    private void OnSessionEnded(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _generation);
        _registeredToken = null;
    }

    /// <summary>
    /// FCM выдал новый токен: регистрируем его, если есть сессия и разрешение.
    /// </summary>
    private void OnTokenRefreshed(object? sender, string token)
    {
        _ = HandleTokenChangedAsync(token);
    }
}
