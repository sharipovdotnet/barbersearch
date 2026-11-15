using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push;

/// <summary>
/// Канал доставки push-уведомлений: одна операция «отправить сообщение на токен устройства».
/// </summary>
/// <remarks>
/// <para>
/// Порт не знает ни про FCM, ни про <c>HttpClient</c>: реализация живёт в
/// <c>BarberSearch.Infrastructure.Push</c>. Без ключей сервисного аккаунта воркер не падает:
/// <see cref="IsConfigured"/> = <c>false</c> означает режим разработки, в котором текст
/// собирается и пишется в журнал, а строка очереди становится <c>Skipped</c>.
/// </para>
/// <para>
/// Ошибка различает три случая, потому что обработчику доставки нужно разное поведение:
/// <list type="bullet">
/// <item><description>
/// <c>push.tokenUnregistered</c> — FCM ответил разобранным <c>UNREGISTERED</c> (или
/// документированным сообщением о недействительном registration token): токен мёртв.
/// Строка токена удаляется, повторять нечего. Не любой <c>404</c>/<c>400</c>: те же коды
/// приходят на неверный проект и кривое тело запроса.
/// </description></item>
/// <item><description>
/// <c>push.unavailable</c> — <c>401/403</c>: проблема с credentials. Попытка повторяется
/// по расписанию 1/5/15 минут, пока их не станет пять.
/// </description></item>
/// <item><description>
/// <c>push.send</c> — сеть, таймаут, <c>429</c> или <c>5xx</c>: временная ошибка отправки,
/// повтор по тому же расписанию.
/// </description></item>
/// </list>
/// Токен и текст сообщения в лог и в текст ошибки не попадают.
/// </para>
/// </remarks>
public interface IPushSender
{
    /// <summary>
    /// Настроен ли транспорт: есть идентификатор проекта и разбираемый сервисный ключ.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Отправляет сообщение на один токен устройства.
    /// </summary>
    /// <param name="message">Готовый заголовок, текст и визит.</param>
    /// <param name="token">Токен устройства из <c>push_device_tokens</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Успех — FCM принял сообщение. Ошибка — <c>push.tokenUnregistered</c> (токен мёртв),
    /// <c>push.unavailable</c> (credentials) либо <c>push.send</c> (временная).
    /// </returns>
    Task<UnitResult<Failure>> SendAsync(
        PushMessage message,
        string token,
        CancellationToken cancellationToken = default);
}
