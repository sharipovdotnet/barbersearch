using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;

namespace BarberSearch.Application.Notifications.UpdateNotificationSettings;

/// <summary>
/// Команда сохранения настроек напоминаний.
/// </summary>
/// <remarks>
/// Владелец настроек — текущий барбер из сессии, поэтому <c>userId</c> в теле запроса
/// не принимается. Запрос заменяет состояние целиком (см.
/// <see cref="UpdateNotificationSettingsRequest"/>), а не правит отдельные поля.
/// </remarks>
/// <param name="Request">Новое состояние настроек.</param>
public record UpdateNotificationSettingsCommand(UpdateNotificationSettingsRequest Request) : ICommand;
