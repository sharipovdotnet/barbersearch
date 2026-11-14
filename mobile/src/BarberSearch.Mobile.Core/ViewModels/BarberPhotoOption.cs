using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Media;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Фотография портфолио, готовая к показу в сетке и в просмотре.
/// </summary>
/// <remarks>
/// <para>
/// Сервер присылает относительные media-URL, а <c>Image</c> ждёт абсолютный: адрес API
/// знает только композиционный корень, поэтому база передаётся в <see cref="From"/>,
/// а не хранится в элементе.
/// </para>
/// <para>
/// Плитка сетки берёт <c>thumbnailUrl</c> — миниатюру, которую сервер уже нарезал, — и
/// только для легаси-ссылок без миниатюры просит вариант <c>size=md</c> у исходника.
/// Просмотр показывает исходник: там картинка занимает весь экран.
/// </para>
/// </remarks>
public sealed class BarberPhotoOption
{
    private BarberPhotoOption(
        Guid id,
        string? description,
        string altText,
        string tileImageUrl,
        string imageUrl)
    {
        Id = id;
        Description = description;
        AltText = altText;
        TileImageUrl = tileImageUrl;
        ImageUrl = imageUrl;
    }

    /// <summary>
    /// Идентификатор фотографии.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Подпись владельца; <c>null</c> — кадр без подписи.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Описание для экранного диктора: подпись либо шаблон с именем и номером.
    /// </summary>
    public string AltText { get; }

    /// <summary>
    /// Абсолютный адрес миниатюры для плитки сетки.
    /// </summary>
    public string TileImageUrl { get; }

    /// <summary>
    /// Абсолютный адрес исходника для полноэкранного просмотра.
    /// </summary>
    public string ImageUrl { get; }

    /// <summary>
    /// Есть ли у кадра подпись для показа под ним.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// Подсказка нажатия для экранного диктора: «Открыть фото: {подпись}».
    /// </summary>
    public string OpenHint => BarberText.PhotoOpenHint(AltText);

    /// <summary>
    /// Собирает фотографию из ответа карточки.
    /// </summary>
    /// <param name="photo">Фотография портфолио из ответа API.</param>
    /// <param name="baseAddress">Базовый адрес API для склейки относительных URL.</param>
    /// <param name="barberName">Имя мастера для шаблона описания.</param>
    /// <param name="number">Номер кадра с единицы — порядок сервера не пересчитывается.</param>
    /// <returns>Кадр для сетки и просмотра.</returns>
    public static BarberPhotoOption From(
        BarberProfilePhotoResponse photo,
        Uri baseAddress,
        string barberName,
        int number)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var tileUrl = string.IsNullOrWhiteSpace(photo.ThumbnailUrl)
            ? MediaUrl.Resize(photo.Url, "md")
            : photo.ThumbnailUrl;

        return new BarberPhotoOption(
            photo.Id,
            photo.Description,
            BarberText.PhotoAlt(barberName, number, photo.Description),
            MediaUrl.Resolve(baseAddress, tileUrl),
            MediaUrl.Resolve(baseAddress, photo.Url));
    }
}
