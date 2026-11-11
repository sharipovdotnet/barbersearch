using BarberSearch.Domain.Common;

namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

/// <summary>
/// Фотография портфолио в карточке барбера.
/// </summary>
/// <remarks>
/// Одна форма на публичную карточку, собственную карточку кабинета и админскую карточку:
/// админский ответ переиспользует публичную форму фото. <c>IsBlocked</c> и <c>IsPublished</c>
/// приходят и в публичном ответе, но после фильтрации там всегда <c>true</c>/<c>false</c>;
/// собственный и админский ответы отдают реальные значения.
/// <para>
/// <see cref="ModerationStatus"/> и <see cref="RejectionReason"/> — премодерация: публичная
/// карточка отдаёт только одобренные кадры, поэтому в публичном ответе статус всегда
/// <see cref="PhotoModerationStatus.Approved"/>, а причина — <c>null</c>. Собственный
/// и админский ответы отдают реальное состояние и текст отказа. <c>reviewedAt</c> наружу
/// не отдаётся: он нужен только в БД для ответственности решения.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор фотографии.</param>
/// <param name="Url">URL фотографии: относительный media-URL или абсолютная внешняя ссылка.</param>
/// <param name="ThumbnailUrl">URL миниатюры либо <c>null</c>: миниатюр в этом срезе нет.</param>
/// <param name="Description">Подпись к фотографии либо <c>null</c>.</param>
/// <param name="SortOrder">Порядковый номер в портфолио, начиная с 0.</param>
/// <param name="IsPublished">Опубликована ли фотография владельцем.</param>
/// <param name="IsBlocked">Заблокирована ли фотография администратором.</param>
/// <param name="ModerationStatus">
/// Допуск кадра к публикации: <c>Pending</c>, <c>Approved</c> или <c>Rejected</c>.
/// Публичный ответ всегда отдаёт <c>Approved</c>.
/// </param>
/// <param name="RejectionReason">
/// Причина отказа либо <c>null</c>. В публичном ответе всегда <c>null</c>; текст
/// администратора в аудит и логи не попадает.
/// </param>
public record BarberProfilePhotoResponse(
    Guid Id,
    string Url,
    string? ThumbnailUrl,
    string? Description,
    int SortOrder,
    bool IsPublished,
    bool IsBlocked,
    PhotoModerationStatus ModerationStatus,
    string? RejectionReason);
