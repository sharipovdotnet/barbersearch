namespace BarberSearch.Contracts.PortfolioPhotos.ReorderPortfolioPhoto;

/// <summary>
/// Данные запроса на изменение порядка вывода фотографии в портфолио.
/// </summary>
/// <remarks>
/// Идентификаторы профиля и фотографии приходят из маршрута, поэтому в теле только
/// <see cref="SortOrder"/>.
/// </remarks>
public record ReorderPortfolioPhotoRequest(int SortOrder);
