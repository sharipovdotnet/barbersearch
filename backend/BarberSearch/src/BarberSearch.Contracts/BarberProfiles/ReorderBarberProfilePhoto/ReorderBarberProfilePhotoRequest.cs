namespace BarberSearch.Contracts.BarberProfiles.ReorderBarberProfilePhoto;

/// <summary>
/// Данные запроса на изменение порядка фотографии в портфолио барбера.
/// </summary>
/// <remarks>
/// Идентификаторы профиля и фотографии приходят из маршрута, поэтому в теле только
/// <see cref="SortOrder"/>.
/// </remarks>
public record ReorderBarberProfilePhotoRequest(int SortOrder);
