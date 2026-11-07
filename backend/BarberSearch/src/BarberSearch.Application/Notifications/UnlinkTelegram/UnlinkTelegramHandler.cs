using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.UnlinkTelegram;

/// <summary>
/// Обработчик отвязки Telegram: очищает чат и всё, что с ним связано, но не историю инбокса.
/// </summary>
/// <remarks>
/// Отсутствие настроек — тоже успех: отвязывать нечего, а клиент не должен получать <c>404</c>
/// на идемпотентную операцию. После отвязки уведомления продолжают приходить в инбокс,
/// просто без сообщений в Telegram.
/// </remarks>
public sealed class UnlinkTelegramHandler : ICommandHandler<UnlinkTelegramCommand>
{
    private readonly IBarberNotificationSettingsRepository _settings;
    private readonly ICurrentUser _currentUser;

    public UnlinkTelegramHandler(
        IBarberNotificationSettingsRepository settings,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(currentUser);

        _settings = settings;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Отвязывает чат Telegram у текущего барбера. Повторный вызов ничего не меняет.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UnlinkTelegramCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var existing = await _settings.FindByUserIdAsync(userId.Value, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value is null)
        {
            return UnitResult.Success<Failure>();
        }

        var unlinked = existing.Value.UnlinkTelegram();
        if (unlinked.IsFailure)
        {
            return unlinked.Error;
        }

        return await _settings.UpdateAsync(existing.Value, cancellationToken);
    }
}
