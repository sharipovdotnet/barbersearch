using BarberSearch.Domain.Common;

namespace BarberSearch.Contracts.Users.Me;

/// <summary>
/// Ответ на загрузку аватара: относительный публичный URL сохранённого файла и состояние
/// проверки нового кадра.
/// </summary>
/// <remarks>
/// <para>
/// Формат — <c>{ "avatarUrl": "/api/media/avatars/{userId}/{mediaId}.{ext}",
/// "moderationStatus": "Pending" }</c>. Поле относительное, как у фотографий портфолио:
/// браузер разрешает его от origin SPA. Заголовок <c>Location</c> не выставляется: у аватара
/// нет отдельного читаемого self-URI, он всегда читается вместе с профилем
/// (<c>GET api/users/me</c>).
/// </para>
/// <para>
/// Премодерация: любая загрузка создаёт <see cref="PhotoModerationStatus.Pending"/>-кадр,
/// поэтому <paramref name="ModerationStatus"/> у ответа всегда <c>Pending</c>. Публично
/// новый кадр не виден до одобрения администратором; владелец видит его в кабинете как
/// превью с бейджем «На проверке».
/// </para>
/// </remarks>
/// <param name="AvatarUrl">Относительный media-URL аватара.</param>
/// <param name="ModerationStatus">
/// Состояние проверки загруженного кадра; у загрузки всегда <c>Pending</c>.
/// </param>
public record AvatarUploadResponse(
    string AvatarUrl,
    PhotoModerationStatus ModerationStatus);
