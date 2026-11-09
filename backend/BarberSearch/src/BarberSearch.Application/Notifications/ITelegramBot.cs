namespace BarberSearch.Application.Notifications;

/// <summary>
/// Публичное состояние Telegram-бота: настроен ли канал и как собрать ссылку привязки.
/// </summary>
/// <remarks>
/// <para>
/// Порт отдельный от <see cref="ITelegramSender"/>: обработчикам настроек нужен только
/// признак «бот настроен» (<c>botConfigured</c>), а выдаче кода привязки — ещё и ссылка
/// <c>https://t.me/{bot}?start={code}</c>. Токен бота и секрет вебхука остаются
/// в <c>BarberSearch.Infrastructure.Telegram</c> и в Application не попадают.
/// </para>
/// <para>
/// Пустой бот — нормальное состояние: канал выключен, инбокс работает, ссылка не выдаётся.
/// Реализация — <c>TelegramOptions</c>; она же регистрируется синглтоном в композиционном корне.
/// </para>
/// </remarks>
public interface ITelegramBot
{
    /// <summary>
    /// Настроен ли канал: заданы и токен, и имя бота. Клиент видит это как <c>botConfigured</c>.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Собирает ссылку <c>https://t.me/{bot}?start={code}</c> для привязки чата.
    /// </summary>
    /// <param name="code">Одноразовый код привязки.</param>
    /// <returns>Готовая ссылка на бота.</returns>
    /// <remarks>
    /// Вызывать только при <see cref="IsConfigured"/>: без имени бота ссылку собрать нельзя,
    /// поэтому реализация выбрасывает <see cref="InvalidOperationException"/>.
    /// </remarks>
    string BuildDeepLink(string code);
}
