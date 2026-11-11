using Microsoft.AspNetCore.Http;

namespace BarberSearch.Web.Controllers.Users;

/// <summary>
/// Форма загрузки аватара пользователя (<c>multipart/form-data</c>, одно поле файла).
/// </summary>
/// <remarks>
/// Живёт в Web, а не в Contracts: Contracts не ссылается на ASP.NET, а <c>IFormFile</c> —
/// его тип. Оригинальное имя файла не передаётся в Application и не логируется: формат
/// определяется по сигнатуре, а имя всё равно не сохраняется.
/// </remarks>
public sealed class UploadUserAvatarForm
{
    /// <summary>
    /// Загружаемый файл. <c>null</c> или пустой файл — <c>400 user.uploadAvatar.fileRequired</c>.
    /// </summary>
    public IFormFile? File { get; init; }
}
