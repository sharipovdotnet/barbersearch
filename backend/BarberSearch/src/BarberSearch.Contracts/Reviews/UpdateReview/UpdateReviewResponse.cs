namespace BarberSearch.Contracts.Reviews.UpdateReview;

/// <summary>
/// Результат правки отзыва: его новый статус публикации.
/// </summary>
/// <remarks>
/// Правка только оценки оставляет отзыв опубликованным, правка текста опубликованного
/// отзыва возвращает его в очередь модерации (R-35). Клиент не выводит это правило сам:
/// он показывает статус, который вернул сервер, иначе строка «Опубликован» врала бы до
/// перезагрузки кабинета.
/// </remarks>
/// <param name="IsPublished">
/// <c>true</c> — отзыв на карточке барбера; <c>false</c> — вернулся на модерацию.
/// </param>
/// <param name="UpdatedAt">Когда отзыв изменён.</param>
public record UpdateReviewResponse(bool IsPublished, DateTimeOffset UpdatedAt);
