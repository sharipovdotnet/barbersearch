using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;
using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.CreateTelegramLink;

/// <summary>
/// Обработчик выдачи кода привязки Telegram: код, хеш в настройках и готовая ссылка на бота.
/// </summary>
/// <remarks>
/// <para>
/// Открытый код существует только в ответе; в базе лежит SHA-256, поэтому утечка таблицы
/// не даёт ссылки. Срок жизни — 10 минут: барбер открывает ссылку сразу, а долгоживущий код
/// — лишняя возможность привязать чужой чат.
/// </para>
/// <para>
/// Повторная выдача при уже привязанном чате — <c>409 notification.telegram.alreadyLinked</c>:
/// смена чата не входит в этот срез, сначала нужно отвязать текущий. Ненастроенный бот —
/// <c>409 notification.telegram.notConfigured</c>, а не ссылка на несуществующего бота.
/// </para>
/// </remarks>
public sealed class CreateTelegramLinkHandler
    : ICommandHandler<TelegramLinkResponse, CreateTelegramLinkCommand>
{
    private readonly IBarberNotificationSettingsRepository _settings;
    private readonly ICurrentUser _currentUser;
    private readonly ITelegramBot _telegramBot;
    private readonly TimeProvider _timeProvider;

    public CreateTelegramLinkHandler(
        IBarberNotificationSettingsRepository settings,
        ICurrentUser currentUser,
        ITelegramBot telegramBot,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(telegramBot);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _settings = settings;
        _currentUser = currentUser;
        _telegramBot = telegramBot;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Выдаёт новый код привязки и ссылку <c>https://t.me/{bot}?start={code}</c>.
    /// </summary>
    public async Task<Result<TelegramLinkResponse, Failure>> Handle(
        CreateTelegramLinkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        if (!_telegramBot.IsConfigured)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Notification.TelegramNotConfigured,
                "Telegram is not configured on the server."));
        }

        var existing = await _settings.FindByUserIdAsync(userId.Value, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var isNew = existing.Value is null;

        var settings = existing.Value;
        if (settings is null)
        {
            var created = BarberNotificationSettings.CreateDefault(userId.Value);
            if (created.IsFailure)
            {
                return created.Error;
            }

            settings = created.Value;
        }

        if (settings.IsTelegramLinked)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Notification.TelegramAlreadyLinked,
                "The Telegram chat is already linked to these notification settings."));
        }

        var code = TelegramLinkCode.Generate();
        var expiresAt = _timeProvider
            .GetUtcNow()
            .AddMinutes(Constants.TELEGRAM_LINK_CODE_LIFETIME_MINUTES);

        var issued = settings.IssueTelegramCode(TelegramLinkCode.Hash(code), expiresAt);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var saved = isNew
            ? await _settings.AddAsync(settings, cancellationToken)
            : await _settings.UpdateAsync(settings, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        return new TelegramLinkResponse(code, _telegramBot.BuildDeepLink(code), expiresAt);
    }
}
