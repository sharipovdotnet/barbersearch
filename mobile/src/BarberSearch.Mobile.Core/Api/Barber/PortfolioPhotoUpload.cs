namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// Подготовленный к загрузке файл портфолио и его подпись.
/// </summary>
/// <remarks>
/// <para>
/// Байты приходят от <see cref="BarberSearch.Mobile.Core.Media.IPortfolioPhotoPicker"/>:
/// приложение уже уменьшило
/// кадр и перекодировало его в JPEG, поэтому клиент не перепроверяет формат и размер —
/// это дело подготовки и сервера. Имя файла на сервер не попадает в качестве ссылки, но
/// multipart-часть должна быть именованной: без неё некоторые прокси и ASP.NET Core
/// разбирают форму иначе.
/// </para>
/// <para>
/// <see cref="Description"/> = <c>null</c> означает «без подписи»; пустая строка при
/// отправке приводится к пустой части формы, потому что поле <c>description</c> в
/// multipart необязательно, но присутствовать должно всегда.
/// </para>
/// </remarks>
/// <param name="Content">Байты подготовленного JPEG.</param>
/// <param name="FileName">Имя файла для multipart-части.</param>
/// <param name="ContentType">MIME-тип содержимого, обычно <c>image/jpeg</c>.</param>
/// <param name="Description">Подпись либо <c>null</c>, если её нет.</param>
public sealed record PortfolioPhotoUpload(
    byte[] Content,
    string FileName,
    string ContentType,
    string? Description);
