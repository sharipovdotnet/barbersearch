using Microsoft.AspNetCore.Http;

namespace BarberSearch.Web.Controllers.PortfolioPhotos;

/// <summary>
/// Форма загрузки фотографии портфолио (<c>multipart/form-data</c>, одно поле файла).
/// </summary>
/// <remarks>
/// Живёт в Web, а не в Contracts: Contracts не ссылается на ASP.NET, а <c>IFormFile</c> —
/// его тип. Оригинальное имя файла не передаётся в Application и не логируется: формат
/// определяется по сигнатуре, а имя всё равно не сохраняется.
/// </remarks>
public sealed class UploadPortfolioPhotoForm
{
    /// <summary>
    /// Загружаемый файл. <c>null</c> или пустой файл — <c>400 barberProfile.uploadPhoto.fileRequired</c>.
    /// </summary>
    public IFormFile? File { get; init; }

    /// <summary>
    /// Необязательная подпись к фотографии; пустая строка домен превращает в <c>null</c>.
    /// </summary>
    public string? Description { get; init; }
}
