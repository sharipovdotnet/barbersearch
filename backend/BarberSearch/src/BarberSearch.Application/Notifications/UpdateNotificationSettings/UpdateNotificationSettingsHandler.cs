using System.Globalization;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.UpdateNotificationSettings;

/// <summary>
/// Обработчик сохранения настроек напоминаний.
/// </summary>
/// <remarks>
/// <para>
/// Время дайджеста приходит строкой <c>HH:mm</c> и разбирается здесь; формат проверяется
/// до домена, потому что агрегат работает с <see cref="TimeOnly"/> и не различает «неверный
/// формат» и «вне диапазона». Оба случая отвечают одним публичным кодом
/// <c>notificationSettings.digestTime</c> (<c>400</c>), а интервал напоминания —
/// <c>notificationSettings.leadMinutes</c>.
/// </para>
/// <para>
/// Настроек может ещё не быть: <c>PUT</c> до первого <c>GET</c> создаёт строку по умолчанию
/// и сразу применяет присланные значения, чтобы клиент не зависел от порядка запросов.
/// </para>
/// </remarks>
public sealed class UpdateNotificationSettingsHandler
    : ICommandHandler<NotificationSettingsResponse, UpdateNotificationSettingsCommand>
{
    private readonly IBarberNotificationSettingsRepository _settings;
    private readonly ICurrentUser _currentUser;
    private readonly TelegramOptions _telegramOptions;

    public UpdateNotificationSettingsHandler(
        IBarberNotificationSettingsRepository settings,
        ICurrentUser currentUser,
        TelegramOptions telegramOptions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(telegramOptions);

        _settings = settings;
        _currentUser = currentUser;
        _telegramOptions = telegramOptions;
    }

    /// <summary>
    /// Валидирует и сохраняет настройки текущего барбера.
    /// </summary>
    public async Task<Result<NotificationSettingsResponse, Failure>> Handle(
        UpdateNotificationSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        if (!TryParseDigestTime(command.Request.DigestTime, out var digestTime))
        {
            return new Failure(Error.Validation(
                BarberNotificationSettings.DIGEST_TIME_CODE,
                "The digest time must be a 'HH:mm' value, for example '08:00'.",
                nameof(command.Request.DigestTime)));
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

        var updated = settings.Update(
            command.Request.IsEnabled,
            command.Request.DigestEnabled,
            digestTime,
            command.Request.LeadMinutes);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = isNew
            ? await _settings.AddAsync(settings, cancellationToken)
            : await _settings.UpdateAsync(settings, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        return NotificationMapping.ToSettingsResponse(settings, _telegramOptions.IsConfigured);
    }

    /// <summary>
    /// Разбирает время дайджеста строго в формате <c>HH:mm</c>. Секунды и другие форматы
    /// отклоняются, чтобы контракт не принимал два разных вида одного значения.
    /// </summary>
    private static bool TryParseDigestTime(string? value, out TimeOnly digestTime)
    {
        digestTime = default;

        return !string.IsNullOrWhiteSpace(value)
            && TimeOnly.TryParseExact(
                value.Trim(),
                NotificationMapping.DIGEST_TIME_FORMAT,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out digestTime);
    }
}
