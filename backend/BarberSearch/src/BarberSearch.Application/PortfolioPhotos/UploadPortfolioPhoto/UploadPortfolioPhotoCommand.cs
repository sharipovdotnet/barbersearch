using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.UploadPortfolioPhoto;

/// <summary>
/// Команда загрузки фотографии в портфолио барбера.
/// </summary>
/// <remarks>
/// Содержимое уже прочитано контроллером в массив: Application не знает ни про
/// <c>IFormFile</c>, ни про multipart. Оригинальное имя файла не передаётся и не логируется —
/// формат определяется по сигнатуре, а не по имени.
/// </remarks>
/// <param name="BarberProfileId">Профиль, в портфолио которого добавляется фотография.</param>
/// <param name="Content">Содержимое файла (не больше <c>Constants.MAX_PHOTO_BYTES</c>).</param>
/// <param name="Description">Необязательная подпись.</param>
public record UploadPortfolioPhotoCommand(
    Guid BarberProfileId,
    byte[] Content,
    string? Description) : ICommand;
