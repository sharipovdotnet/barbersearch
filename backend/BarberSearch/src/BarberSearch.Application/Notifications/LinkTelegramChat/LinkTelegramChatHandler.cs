using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Notifications.LinkTelegramChat;

/// <summary>
/// Обработчик привязки чата Telegram по одноразовому коду.
/// </summary>
/// <remarks>
/// <para>
/// Порядок шагов повторяет §6.1 спецификации: идемпотентность по <c>update_id</c>, поиск
/// настроек по хешу и сроку кода, проверка «чат уже привязан к другому кабинету», затем
/// <c>LinkTelegram</c>. Во всех случаях, включая неуспех, бот отвечает сообщением, а вебхук
/// отвечает <c>200</c>: Telegram ретраит не-<c>2xx</c>, и повтор не должен менять состояние.
/// </para>
/// <para>
/// Неизвестный или истёкший код — не ошибка (код одноразовый и живёт 10 минут), а повод
/// отправить «Код устарел…». Отправка ответа не влияет на успех привязки: если Telegram
/// недоступен, чат всё равно остаётся привязанным, а сообщение можно получить в кабинете.
/// </para>
/// </remarks>
public sealed class LinkTelegramChatHandler : ICommandHandler<LinkTelegramChatCommand>
{
    private readonly ITelegramUpdateRepository _updates;
    private readonly IBarberNotificationSettingsRepository _settings;
    private readonly ITelegramSender _telegram;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LinkTelegramChatHandler> _logger;

    public LinkTelegramChatHandler(
        ITelegramUpdateRepository updates,
        IBarberNotificationSettingsRepository settings,
        ITelegramSender telegram,
        TimeProvider timeProvider,
        ILogger<LinkTelegramChatHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(telegram);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _updates = updates;
        _settings = settings;
        _telegram = telegram;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Обрабатывает обновление бота: регистрирует его, находит код и привязывает чат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        LinkTelegramChatCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _timeProvider.GetUtcNow();

        var registered = await _updates.TryRegisterAsync(command.UpdateId, now, cancellationToken);
        if (registered.IsFailure)
        {
            return registered.Error;
        }

        if (!registered.Value)
        {
            // Обновление уже обработано — вебхук всё равно ответит 200.
            return UnitResult.Success<Failure>();
        }

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            // Бот получил не /start: привязывать нечего, отвечать не нужно.
            return UnitResult.Success<Failure>();
        }

        var hash = TelegramLinkCode.Hash(command.Code);

        var settingsResult = await _settings.FindByTelegramLinkCodeHashAsync(
            hash,
            now,
            cancellationToken);
        if (settingsResult.IsFailure)
        {
            return settingsResult.Error;
        }

        var settings = settingsResult.Value;

        if (settings is null)
        {
            await SendAsync(
                command.ChatId,
                NotificationMessages.TELEGRAM_LINK_CODE_EXPIRED,
                cancellationToken);

            return UnitResult.Success<Failure>();
        }

        var linkedToAnother = await _settings.IsChatLinkedToAnotherUserAsync(
            command.ChatId,
            settings.UserId,
            cancellationToken);
        if (linkedToAnother.IsFailure)
        {
            return linkedToAnother.Error;
        }

        if (linkedToAnother.Value)
        {
            await SendAsync(
                command.ChatId,
                NotificationMessages.TELEGRAM_CHAT_ALREADY_LINKED,
                cancellationToken);

            return UnitResult.Success<Failure>();
        }

        var linked = settings.LinkTelegram(command.ChatId, now);
        if (linked.IsFailure)
        {
            return linked.Error;
        }

        var saved = await _settings.UpdateAsync(settings, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await SendAsync(
            command.ChatId,
            NotificationMessages.TELEGRAM_LINKED,
            cancellationToken);

        return UnitResult.Success<Failure>();
    }

    /// <summary>
    /// Отправляет ответ бота. Неудача отправки логируется без текста и чата: привязка уже
    /// выполнена, а сообщение — следствие, а не условие.
    /// </summary>
    private async Task SendAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken)
    {
        var sent = await _telegram.SendMessageAsync(chatId, text, cancellationToken);
        if (sent.IsFailure)
        {
            // Коды ошибок стабильны и не содержат ни текста сообщения, ни идентификатора чата.
            _logger.LogWarning(
                "Telegram reply for a chat link update was not delivered: {Code}.",
                sent.Error.Errors.Count > 0 ? sent.Error.Errors[0].Code : "unknown");
        }
    }
}
