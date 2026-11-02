namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Самый старый отзыв очереди модерации, компактно для сводки.
/// </summary>
/// <remarks>
/// Это не <c>AdminReviewResponse</c>: экрану нужна одна строка «сколько ждём и о чём»,
/// а не весь визит и не email клиента. <see cref="CreatedAt"/> — UTC: в интерфейсе ожидание
/// показывается словами, а не датой, потому что это не визит и пояс салона здесь не применим.
/// <see cref="CustomerDisplayName"/> — то же <c>FullName</c>, что показывает очередь; при пустом
/// имени оно равно email — это существующее поведение админского контракта, отдельного
/// <c>email</c> в сводке нет.
/// </remarks>
/// <param name="Id">Идентификатор отзыва — ссылка на карточку в очереди.</param>
/// <param name="Rating">Оценка от 1 до 5.</param>
/// <param name="CreatedAt">Когда отзыв создан (UTC).</param>
/// <param name="BarberDisplayName">Имя барбера, которому оставлен отзыв.</param>
/// <param name="CustomerDisplayName">Имя клиента — автора отзыва.</param>
public record AdminSummaryOldestReviewResponse(
    Guid Id,
    int Rating,
    DateTimeOffset CreatedAt,
    string BarberDisplayName,
    string CustomerDisplayName);
