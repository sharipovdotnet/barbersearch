using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Канал доставки в Telegram: одна операция «отправить текст в чат».
/// </summary>
/// <remarks>
/// <para>
/// Порт не знает ни про <c>HttpClient</c>, ни про Bot API: реализация живёт
/// в <c>BarberSearch.Infrastructure.Telegram</c>, где настраивается <c>Telegram:BotToken</c>.
/// Без токена воркер не падает — отправка просто возвращает ошибку, а уведомление остаётся
/// в инбоксе.
/// </para>
/// <para>
/// Ошибка различает два случая, потому что обработчику нужно разное поведение:
/// <list type="bullet">
/// <item><description>
/// <c>notification.telegram.chatUnavailable</c> — Telegram ответил <c>400</c>/<c>403</c>:
/// бот заблокирован, чат не найден. Повторять бессмысленно, поэтому уведомление получает
/// <c>Skipped</c>, а настройки — <c>UnlinkTelegram()</c>.
/// </description></item>
/// <item><description>
/// <c>notification.telegram.send</c> — сеть, таймаут, <c>429</c> или <c>5xx</c>: попытка
/// повторяется по расписанию 1/5/15 минут, пока их не станет пять.
/// </description></item>
/// </list>
/// Текст ошибки попадает в <c>BarberNotification.LastError</c> как диагностика: имени,
/// телефона и заметок клиента в нём быть не должно (§6.2 спецификации).
/// </para>
/// </remarks>
public interface ITelegramSender
{
    /// <summary>
    /// Отправляет текст в чат Telegram.
    /// </summary>
    /// <param name="chatId">Идентификатор чата из настроек барбера.</param>
    /// <param name="text">Готовый текст сообщения без ПДн клиента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Успех — сообщение принято Telegram. Ошибка — <c>notification.telegram.chatUnavailable</c>
    /// (повторять нельзя) либо <c>notification.telegram.send</c> (временная, повторяем).
    /// </returns>
    Task<UnitResult<Failure>> SendMessageAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken = default);
}
