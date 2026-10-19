namespace BarberSearch.Contracts.Common;

/// <summary>
/// Страница выдачи с непрозрачным курсором следующей страницы.
/// </summary>
/// <remarks>
/// Курсор — это часть контракта пагинации, а не внутренний идентификатор записи:
/// клиент передаёт его обратно как есть и не должен разбирать его содержимое.
/// <see cref="NextCursor"/> = <c>null</c> означает, что страница последняя.
/// </remarks>
/// <typeparam name="T">Тип элемента страницы.</typeparam>
/// <param name="Items">Элементы текущей страницы; может быть пустым.</param>
/// <param name="NextCursor">Курсор следующей страницы либо <c>null</c>, если страниц больше нет.</param>
public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);
