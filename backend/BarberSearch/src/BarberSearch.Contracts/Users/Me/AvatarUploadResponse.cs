namespace BarberSearch.Contracts.Users.Me;

/// <summary>
/// Ответ на загрузку аватара: относительный публичный URL сохранённого файла.
/// </summary>
/// <remarks>
/// Формат — <c>{ "avatarUrl": "/api/media/avatars/{userId}/{mediaId}.{ext}" }</c>. Поле
/// относительное, как у фотографий портфолио: браузер разрешает его от origin SPA.
/// Заголовок <c>Location</c> не выставляется: у аватара нет отдельного читаемого self-URI,
/// он всегда читается вместе с профилем (<c>GET api/users/me</c>).
/// </remarks>
/// <param name="AvatarUrl">Относительный media-URL аватара.</param>
public record AvatarUploadResponse(string AvatarUrl);
