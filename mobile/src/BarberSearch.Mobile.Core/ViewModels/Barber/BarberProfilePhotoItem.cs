using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Фотография портфолио в собственном кабинете: адрес, подпись и состояние проверки.
/// </summary>
/// <remarks>
/// <para>
/// В этом срезе фотографии только читаются: загрузка, подпись и порядок остаются вебу.
/// Поэтому строка не держит команд, а её задача — честно показать, почему кадр не виден
/// клиенту: отклонён администратором, ждёт проверки или скрыт владельцем.
/// </para>
/// <para>
/// Адреса отдаются как есть: склейку с базовым адресом API делает страница через
/// <c>MediaUrl.Resolve</c>, потому что Core не знает адреса сервера.
/// </para>
/// </remarks>
public sealed class BarberProfilePhotoItem
{
    /// <summary>
    /// Создаёт строку из фотографии карточки.
    /// </summary>
    /// <param name="photo">Фотография из собственного ответа.</param>
    public BarberProfilePhotoItem(BarberProfilePhotoResponse photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        Id = photo.Id;
        Url = photo.Url;
        ThumbnailUrl = photo.ThumbnailUrl;
        Caption = BarberProfileText.PhotoCaption(photo.Description);
        HasCaption = !string.IsNullOrWhiteSpace(photo.Description);
        SortOrder = photo.SortOrder;
        IsPublished = photo.IsPublished;
        IsBlocked = photo.IsBlocked;
        ModerationStatus = photo.ModerationStatus;
        RejectionReason = photo.RejectionReason;
        StatusText = BarberProfileText.PhotoStatusText(photo);
    }

    /// <summary>
    /// Идентификатор фотографии.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Адрес фотографии из ответа API.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Адрес миниатюры; <c>null</c> у внешних ссылок.
    /// </summary>
    public string? ThumbnailUrl { get; }

    /// <summary>
    /// Подпись фотографии либо «Без подписи».
    /// </summary>
    public string Caption { get; }

    /// <summary>
    /// Подпись заполнена владельцем.
    /// </summary>
    public bool HasCaption { get; }

    /// <summary>
    /// Порядковый номер в портфолио.
    /// </summary>
    public int SortOrder { get; }

    /// <summary>
    /// Опубликована ли фотография владельцем.
    /// </summary>
    public bool IsPublished { get; }

    /// <summary>
    /// Скрыта ли фотография администратором.
    /// </summary>
    public bool IsBlocked { get; }

    /// <summary>
    /// Допуск кадра к публикации.
    /// </summary>
    public PhotoModerationStatus ModerationStatus { get; }

    /// <summary>
    /// Причина отказа администратора; <c>null</c>, если отказа нет.
    /// </summary>
    public string? RejectionReason { get; }

    /// <summary>
    /// Состояние строки: «Опубликовано», «На проверке», «Отклонено» и соседние.
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Виден ли кадр клиенту: опубликован, одобрен и не скрыт.
    /// </summary>
    public bool IsVisibleToClients =>
        IsPublished && !IsBlocked && ModerationStatus == PhotoModerationStatus.Approved;
}
