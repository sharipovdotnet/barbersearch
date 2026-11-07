using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Notifications;

namespace BarberSearch.Application.Notifications.GetUnreadCount;

/// <summary>
/// Запрос числа непрочитанных уведомлений для бейджа.
/// </summary>
/// <remarks>
/// Параметров нет: барбер берётся из сессии. Запрос дешёвый и вызывается при каждом
/// монтировании кабинета, поэтому ответ — только счётчик.
/// </remarks>
public sealed record GetUnreadCountQuery : IQuery<UnreadNotificationsCountResponse>;
