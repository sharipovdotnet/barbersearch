using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Notifications.MarkAllNotificationsRead;

/// <summary>
/// Команда «прочитать все уведомления».
/// </summary>
/// <remarks>
/// Параметров нет: получатель — текущий барбер из сессии. Операция идемпотентна, поэтому
/// повторный вызов после сброса бейджа — успех, а не <c>404</c> или <c>409</c>.
/// </remarks>
public sealed record MarkAllNotificationsReadCommand : ICommand;
