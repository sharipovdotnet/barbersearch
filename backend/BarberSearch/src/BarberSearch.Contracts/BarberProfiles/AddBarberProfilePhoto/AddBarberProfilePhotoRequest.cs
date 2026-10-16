namespace BarberSearch.Contracts.BarberProfiles.AddBarberProfilePhoto;

/// <summary>
/// Данные запроса на добавление фотографии в портфолио барбера.
/// </summary>
/// <remarks>
/// Файл сюда не передаётся — только готовые ссылки: загрузкой в хранилище занимается
/// отдельный сервис, а агрегат хранит <see cref="Url"/> и необязательный <see cref="ThumbnailUrl"/>.
/// </remarks>
public record AddBarberProfilePhotoRequest(
    string Url,
    string? ThumbnailUrl,
    string? Description);
