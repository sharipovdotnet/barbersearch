using System.Net;
using System.Net.Http.Headers;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;

namespace BarberSearch.Mobile.Core.Auth;

/// <summary>
/// Подставляет Bearer-токен и переживает истечение access-токена одним refresh и одним повтором.
/// </summary>
/// <remarks>
/// <para>
/// Проактивного продления по времени нет: приложение не хранит срок жизни токена и узнаёт
/// о нём только по <c>401</c>. Так клиентские часы не участвуют в решении, а лишних запросов
/// к серверу не появляется.
/// </para>
/// <para>
/// Refresh строго один на все параллельные <c>401</c>: сервер ротирует refresh при каждом
/// обмене, и два одновременных обмена предъявили бы один и тот же токен — второй запрос
/// получил бы <c>auth.refresh.reused</c> и сервер отозвал бы всю цепочку. Поэтому обмен
/// идёт под <see cref="SemaphoreSlim"/>, а после ожидания пара перепроверяется: возможно,
/// её уже обновил соседний запрос.
/// </para>
/// <para>
/// Отказ сервера (<c>401</c>) — единственный повод стирать сессию. <c>429</c>, <c>5xx</c>
/// и обрыв сети — временные: токен жив, и через минуту тот же запрос пройдёт.
/// </para>
/// </remarks>
public sealed class AuthenticatedHttpHandler : DelegatingHandler
{
    private readonly IAuthApi _authApi;
    private readonly TokenSession _session;

    /// <summary>
    /// Один обмен на все запросы, получившие <c>401</c> одновременно.
    /// </summary>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// Создаёт обработчик поверх сессии.
    /// </summary>
    /// <param name="authApi">Клиент refresh; намеренно без этого обработчика в цепочке.</param>
    /// <param name="session">Сессия, из которой берётся и в которую пишется пара.</param>
    public AuthenticatedHttpHandler(IAuthApi authApi, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(authApi);
        ArgumentNullException.ThrowIfNull(session);

        _authApi = authApi;
        _session = session;
    }

    /// <summary>
    /// Ставит заголовок, отправляет запрос и при <c>401</c> повторяет его после обновления пары.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ сервера; второй <c>401</c> уходит наружу без повторов.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _session.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // Токен, с которым ушёл запрос: по нему видно, обновил ли пару соседний запрос,
        // пока этот ждал свою очередь на refresh.
        var tokenUsed = _session.AccessToken;

        if (tokenUsed is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenUsed);
        }

        // Тело приходится запомнить до отправки: повтору нужен новый HttpRequestMessage,
        // а первый уже израсходовал исходный контент.
        var content = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized || tokenUsed is null)
        {
            return response;
        }

        if (!await TryRefreshAsync(tokenUsed, cancellationToken).ConfigureAwait(false))
        {
            return response;
        }

        var retry = Clone(request, content);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);

        response.Dispose();

        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Обменивает refresh на новую пару, если её ещё не обновил другой запрос.
    /// </summary>
    /// <param name="tokenUsed">Токен, с которым ушёл получивший <c>401</c> запрос.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если в сессии есть пара новее использованной.</returns>
    private async Task<bool> TryRefreshAsync(string tokenUsed, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_session.AccessToken != tokenUsed && _session.AccessToken is not null)
            {
                // Пока запрос ждал, пару обновил соседний: второй обмен предъявил бы
                // уже ротированный refresh и сервер отозвал бы цепочку.
                return true;
            }

            var refreshToken = _session.RefreshToken;

            if (refreshToken is null)
            {
                return false;
            }

            try
            {
                var tokens = await _authApi.RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);

                await _session.ApplyAsync(tokens, cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Токен мёртв: повтор не поможет, сессию надо начинать заново. Очистка
                // намеренно не отменяется вместе с запросом (CancellationToken.None):
                // отменённый refresh оставил бы мёртвую пару в хранилище навсегда.
                await _session.ClearAsync(CancellationToken.None).ConfigureAwait(false);

                return false;
            }
            catch (BarberSearchApiException)
            {
                // 429 и 5xx: сервер не отказал в токене, просто не смог ответить сейчас.
                return false;
            }
            catch (HttpRequestException)
            {
                // Сеть: refresh не дошёл, токен не предъявлен, цепочка цела.
                return false;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Собирает запрос для повтора из метода, адреса, заголовков и запомненного тела.
    /// </summary>
    /// <remarks>
    /// Заголовок авторизации копируется вместе с остальными и сразу перезаписывается новым
    /// токеном — так у повтора не остаётся шанса уйти со старым.
    /// </remarks>
    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? content)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (content is not null && request.Content is not null)
        {
            var body = new ByteArrayContent(content);

            foreach (var header in request.Content.Headers)
            {
                body.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = body;
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
    }
}
