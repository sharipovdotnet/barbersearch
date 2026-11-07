using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Notifications.LinkTelegramChat;

/// <summary>
/// Команда вебхука Telegram: привязать чат по одноразовому коду из сообщения боту.
/// </summary>
/// <remarks>
/// <para>
/// Идентификатор обновления передаётся вместе с данными, потому что идемпотентность —
/// часть операции: повторный <c>update_id</c> не должен повторно привязывать чат и
/// отправлять ответ. Код может отсутствовать: бот получает и обычные сообщения, на которые
/// не реагирует.
/// </para>
/// <para>
/// Команду вызывает Web после проверки секрета вебхука; Application ничего не знает
/// ни про HTTP, ни про заголовок <c>X-Telegram-Bot-Api-Secret-Token</c>.
/// </para>
/// </remarks>
/// <param name="UpdateId">Идентификатор обновления Bot API.</param>
/// <param name="ChatId">Чат, из которого пришло сообщение.</param>
/// <param name="Code">Код из команды <c>/start</c> или <c>null</c>, если его нет.</param>
public sealed record LinkTelegramChatCommand(
    long UpdateId,
    long ChatId,
    string? Code) : ICommand;
