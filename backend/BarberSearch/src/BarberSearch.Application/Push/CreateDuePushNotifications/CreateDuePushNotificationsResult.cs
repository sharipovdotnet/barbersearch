namespace BarberSearch.Application.Push.CreateDuePushNotifications;

/// <summary>
/// Итог тика создания push-напоминаний.
/// </summary>
/// <remarks>
/// Считаются только реально вставленные строки: повтор по уникальному индексу
/// <c>ux_push_notifications_booking_id</c> не увеличивает счётчик, поэтому повторный тик
/// и второй экземпляр приложения дают <c>Created = 0</c>, а не дубли.
/// </remarks>
/// <param name="Created">Сколько напоминаний вставлено за тик.</param>
/// <param name="SkippedLate">
/// Сколько визитов пропущено, потому что до начала осталось заметно меньше окна: после
/// простоя напоминание «через час» за десять минут до визита было бы ложью.
/// </param>
public sealed record CreateDuePushNotificationsResult(int Created, int SkippedLate);
