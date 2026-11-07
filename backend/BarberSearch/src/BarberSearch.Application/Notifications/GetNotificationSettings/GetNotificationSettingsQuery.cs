using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Notifications;

namespace BarberSearch.Application.Notifications.GetNotificationSettings;

/// <summary>
/// Запрос настроек напоминаний текущего барбера.
/// </summary>
/// <remarks>
/// Параметров нет: владелец настроек — пользователь из сессии. Первое чтение создаёт
/// настройки по умолчанию, поэтому клиент никогда не видит <c>404</c> на этом маршруте.
/// </remarks>
public sealed record GetNotificationSettingsQuery : IQuery<NotificationSettingsResponse>;
