using BarberSearch.Contracts.Auth.Login;

namespace BarberSearch.Mobile.Core.Auth;

/// <summary>
/// Сессия приложения: единственный владелец пары токенов в памяти.
/// </summary>
/// <remarks>
/// <para>
/// Состояние ленивое: хранилище читается при первом обращении, а не в конструкторе, потому что
/// конструктор не может быть асинхронным. Нужна именно пара, а не отдельные токены: сервер
/// ротирует refresh при каждом обмене, и половина пары быстро становится мёртвой.
/// </para>
/// <para>
/// <see cref="SessionEnded"/> поднимается только при переходе из «вошли» в «не вошли»:
/// выход и отказ сервера продлить пару. Экран по нему возвращается к форме входа; сам Core
/// про MAUI не знает, поэтому событие не пытается переключиться на UI-поток — это делает
/// подписчик на стороне приложения.
/// </para>
/// </remarks>
public sealed class TokenSession : IDisposable
{
    private readonly ITokenStore _tokenStore;

    /// <summary>
    /// Сериализует ленивую загрузку и записи: два запроса, стартовавших одновременно, не должны
    /// прочитать хранилище дважды и не должны затереть только что записанную пару.
    /// </summary>
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private StoredTokens? _tokens;
    private bool _isLoaded;

    /// <summary>
    /// Создаёт сессию над хранилищем.
    /// </summary>
    /// <param name="tokenStore">Хранилище пары между запусками.</param>
    public TokenSession(ITokenStore tokenStore)
    {
        ArgumentNullException.ThrowIfNull(tokenStore);

        _tokenStore = tokenStore;
    }

    /// <summary>
    /// Сессия завершена: пары больше нет — вышел пользователь или сервер отказал в продлении.
    /// </summary>
    /// <remarks>
    /// Событие синхронное и поднимается на том потоке, который завершил сессию. Подписчик
    /// на стороне приложения обязан сам перейти на UI-поток: Core не знает про диспетчер MAUI.
    /// </remarks>
    public event EventHandler? SessionEnded;

    /// <summary>
    /// Сессия началась: пара появилась там, где её не было.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Поднимается только на переходе «не вошли» → «вошли», то есть при входе и регистрации,
    /// но не при продлении пары: <c>AuthenticatedHttpHandler</c> применяет новую пару на
    /// каждом refresh, и событие на каждый refresh заставляло бы клиентов заново
    /// регистрировать устройство по несколько раз за сессию.
    /// </para>
    /// <para>
    /// Как и <see cref="SessionEnded"/>, событие синхронное и приходит на сетевом потоке:
    /// push-регистратору UI не нужен, а разрешение на уведомления запрашивает платформенный
    /// сервис, который сам переходит на главный поток.
    /// </para>
    /// </remarks>
    public event EventHandler? SessionStarted;

    /// <summary>
    /// Есть ли действующая пара.
    /// </summary>
    /// <remarks>
    /// До <see cref="EnsureLoadedAsync"/> свойство отражает только состояние в памяти; экран
    /// сначала ждёт загрузку, иначе на старте он на мгновение показал бы форму входа.
    /// </remarks>
    public bool IsAuthenticated => _tokens is not null;

    /// <summary>
    /// Проверяет сессию, при необходимости прочитав её из хранилища.
    /// </summary>
    /// <remarks>
    /// Единственный корректный способ спросить «есть ли вход» у фонового клиента (push-регистратор,
    /// старт приложения): <see cref="IsAuthenticated"/> до <see cref="EnsureLoadedAsync"/> видит
    /// только память и на первом запуске ответил бы «нет» при живой сохранённой паре.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если пара есть.</returns>
    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        return IsAuthenticated;
    }

    /// <summary>
    /// Текущий access-токен или <c>null</c>, если сессии нет.
    /// </summary>
    public string? AccessToken => _tokens?.AccessToken;

    /// <summary>
    /// Текущий refresh-токен или <c>null</c>, если сессии нет.
    /// </summary>
    public string? RefreshToken => _tokens?.RefreshToken;

    /// <summary>
    /// Читает пару из хранилища ровно один раз.
    /// </summary>
    /// <remarks>
    /// Повторные вызовы ничего не делают: после первой загрузки источником правды становится
    /// память, иначе после выхода (сессия стёрта) следующий обработчик вернул бы старую пару.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded)
        {
            return;
        }

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_isLoaded)
            {
                return;
            }

            _tokens = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            _isLoaded = true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Принимает новую пару от сервера и сохраняет её.
    /// </summary>
    /// <remarks>
    /// Память обновляется до записи: сессия уже действует, и сбой хранилища не должен
    /// оставлять приложение в состоянии «токены есть, но непонятно где».
    /// </remarks>
    /// <param name="tokens">Ответ <c>login</c> или <c>refresh</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="InvalidOperationException">
    /// Сервер вернул access-токен без refresh: для внешнего клиента это нарушение контракта,
    /// и продолжать с половиной пары нельзя.
    /// </exception>
    public async Task ApplyAsync(TokenResponse tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            throw new InvalidOperationException("The API returned an access token without a refresh token.");
        }

        var stored = new StoredTokens(tokens.AccessToken, tokens.RefreshToken);

        bool wasAuthenticated;

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            wasAuthenticated = _tokens is not null;
            _tokens = stored;
            _isLoaded = true;
        }
        finally
        {
            _stateLock.Release();
        }

        await _tokenStore.SaveAsync(stored, cancellationToken).ConfigureAwait(false);

        if (!wasAuthenticated)
        {
            SessionStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Завершает сессию и стирает пару.
    /// </summary>
    /// <remarks>
    /// Хранилище чистится в любом случае, даже если в памяти пары уже нет: refresh мог остаться
    /// с прошлого запуска, когда загрузка ещё не успела произойти.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        bool wasAuthenticated;

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            wasAuthenticated = _tokens is not null;
            _tokens = null;
            _isLoaded = true;
        }
        finally
        {
            _stateLock.Release();
        }

        await _tokenStore.ClearAsync(cancellationToken).ConfigureAwait(false);

        if (wasAuthenticated)
        {
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Освобождает семафор состояния.
    /// </summary>
    /// <remarks>
    /// Сессия живёт столько же, сколько приложение (singleton в композиционном корне), поэтому
    /// метод нужен скорее анализатору и тестам, чем рабочему коду: освобождать есть что,
    /// а держать <see cref="SemaphoreSlim"/> без владельца — утечка.
    /// </remarks>
    public void Dispose() => _stateLock.Dispose();
}
