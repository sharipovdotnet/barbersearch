namespace BarberSearch.Contracts.BarberProfiles.ReorderBarberProfilePhotos;

/// <summary>
/// Запрос порядка фотографий портфолио: полный список идентификаторов в новом порядке.
/// </summary>
/// <remarks>
/// Список обязан содержать ровно все фотографии профиля без дублей; индекс элемента
/// становится <c>SortOrder</c>. Частичный список отклоняется, чтобы порядок не поехал.
/// </remarks>
/// <param name="PhotoIds">Идентификаторы фотографий в новом порядке.</param>
public record ReorderBarberProfilePhotosRequest(IReadOnlyList<Guid> PhotoIds);
