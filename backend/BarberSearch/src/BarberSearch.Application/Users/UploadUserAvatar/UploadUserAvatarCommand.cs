using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.UploadUserAvatar;

/// <summary>
/// Команда загрузки аватара текущего пользователя.
/// </summary>
/// <remarks>
/// Содержимое уже прочитано контроллером в массив: Application не знает ни про
/// <c>IFormFile</c>, ни про multipart. Оригинальное имя файла не передаётся и не логируется —
/// формат определяется по сигнатуре, а не по имени. Идентификатор пользователя берётся из
/// claim'а <c>sub</c>, а не из команды: аватар всегда свой.
/// </remarks>
/// <param name="Content">Содержимое файла (не больше <c>Constants.MAX_PHOTO_BYTES</c>).</param>
public record UploadUserAvatarCommand(ReadOnlyMemory<byte> Content) : ICommand;
