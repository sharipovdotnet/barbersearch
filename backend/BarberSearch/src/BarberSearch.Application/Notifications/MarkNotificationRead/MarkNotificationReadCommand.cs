using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Notifications.MarkNotificationRead;

/// <summary>
/// Команда «прочитать уведомление».
/// </summary>
/// <remarks>
/// Отметка идемпотентна: повторный клик по уже прочитанной строке — успех, а не ошибка.
/// Идентификатор уведомления, а не барбера: получатель берётся из сессии, поэтому чужое
/// уведомление для обработчика «не найдено» (<c>404 notification.notFound</c>).
/// </remarks>
/// <param name="NotificationId">Идентификатор уведомления.</param>
public record MarkNotificationReadCommand(Guid NotificationId) : ICommand;
