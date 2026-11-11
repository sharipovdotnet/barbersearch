namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

/// <summary>
/// Фотография портфолио в карточке барбера.
/// </summary>
/// <remarks>
/// Одна форма на публичную карточку, собственную карточку кабинета и админскую карточку:
/// админский ответ переиспользует публичную форму фото. <c>IsBlocked</c> и <c>IsPublished</c>
/// приходят и в публичном ответе, но после фильтрации там всегда <c>true</c>/<c>false</c>;
/// собственный и админский ответы отдают реальные значения.
/// </remarks>
/// <param name="Id">Идентификатор фотографии.</param>
/// <param name="Url">URL фотографии: относительный media-URL или абсолютная внешняя ссылка.</param>
/// <param name="ThumbnailUrl">URL миниатюры либо <c>null</c>: миниатюр в этом срезе нет.</param>
/// <param name="Description">Подпись к фотографии либо <c>null</c>.</param>
/// <param name="SortOrder">Порядковый номер в портфолио, начиная с 0.</param>
/// <param name="IsPublished">Опубликована ли фотография владельцем.</param>
/// <param name="IsBlocked">Заблокирована ли фотография администратором.</param>
public record BarberProfilePhotoResponse(
    Guid Id,
    string Url,
    string? ThumbnailUrl,
    string? Description,
    int SortOrder,
    bool IsPublished,
    bool IsBlocked);
