using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications.GetNotificationSettings;

/// <summary>
/// Обработчик чтения настроек напоминаний: создаёт их при первом обращении.
/// </summary>
/// <remarks>
/// <para>
/// Значения по умолчанию — <c>IsEnabled = true</c>, <c>DigestEnabled = true</c>, 08:00
/// локального пояса салона и 30 минут до визита (решения D2 и D8). Отдельной операции
/// «создать настройки» в API нет: клиент всегда получает объект и правит его целиком.
/// </para>
/// <para>
/// Создание гонкой не считается ошибкой: два параллельных первых чтения дают конфликт
/// первичного ключа, и обработчик перечитывает уже созданную строку.
/// </para>
/// <para>
/// <c>botConfigured</c> приходит из конфигурации (<see cref="TelegramOptions.IsConfigured"/>),
/// а не из базы: это состояние сервера, а не настройка барбера. Идентификатор чата наружу
/// не отдаётся — только <c>telegramLinked</c>/<c>telegramLinkedAt</c>.
/// </para>
/// </remarks>
public sealed class GetNotificationSettingsHandler
    : IQueryHandler<NotificationSettingsResponse, GetNotificationSettingsQuery>
{
    private readonly IBarberNotificationSettingsRepository _settings;
    private readonly ICurrentUser _currentUser;
    private readonly TelegramOptions _telegramOptions;

    public GetNotificationSettingsHandler(
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
    /// Возвращает настройки текущего барбера, создавая их по умолчанию при первом чтении.
    /// </summary>
    public async Task<Result<NotificationSettingsResponse, Failure>> Handle(
        GetNotificationSettingsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

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

        var settings = existing.Value;

        if (settings is null)
        {
            var created = BarberNotificationSettings.CreateDefault(userId.Value);
            if (created.IsFailure)
            {
                return created.Error;
            }

            var added = await _settings.AddAsync(created.Value, cancellationToken);

            if (added.IsFailure)
            {
                // Гонка двух первых чтений: строку уже создал параллельный запрос.
                var reread = await _settings.FindByUserIdAsync(userId.Value, cancellationToken);
                if (reread.IsFailure)
                {
                    return reread.Error;
                }

                if (reread.Value is null)
                {
                    return added.Error;
                }

                settings = reread.Value;
            }
            else
            {
                settings = created.Value;
            }
        }

        return NotificationMapping.ToSettingsResponse(settings, _telegramOptions.IsConfigured);
    }
}
