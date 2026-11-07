namespace BarberSearch.Application.Notifications.CreateDueNotifications;

/// <summary>
/// Итог тика создания уведомлений.
/// </summary>
/// <remarks>
/// Считаются только реально вставленные строки: повтор по уникальному индексу (визит уже
/// назван, дайджест за эти сутки уже есть) не увеличивает счётчик, поэтому повторный тик
/// и второй экземпляр приложения дают <c>Created = 0</c>, а не дубли.
/// </remarks>
/// <param name="Created">Сколько уведомлений вставлено за тик.</param>
public sealed record CreateDueNotificationsResult(int Created);
