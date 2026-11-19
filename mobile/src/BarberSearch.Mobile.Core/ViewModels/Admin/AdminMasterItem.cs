using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Строка очереди мастеров: ответ API, разложенный по текстам карточки.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AdminBarberResponse"/> несёт всё, что показывает строка: имя, email, грейд,
/// рейтинг и состояние профиля. Салона, стажа и чек-листа готовности в строке нет — это поля
/// карточки (<see cref="AdminBarberDetailResponse"/>), поэтому очередь не догружает карточку
/// на каждую строку: список остаётся одним запросом.
/// </para>
/// <para>
/// Бейдж аватара повторяет приоритет веба (<c>barber-list.tsx</c>): скрытый администратором
/// кадр важнее отклонённого, а отклонённый — ожидающего проверки. Фото при блокировке
/// показывается: это админская поверхность, и состояние объясняет бейдж.
/// </para>
/// </remarks>
public sealed partial class AdminMasterItem : ObservableObject
{
    /// <summary>
    /// Создаёт строку из ответа API.
    /// </summary>
    /// <param name="master">Строка админского списка.</param>
    /// <param name="apiOptions">Адрес API: от него зависят абсолютные адреса аватара.</param>
    public AdminMasterItem(AdminBarberResponse master, BarberSearchApiOptions apiOptions)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(apiOptions);

        var avatarUrl = master.AvatarUrl;

        Id = master.Id;
        DisplayName = master.DisplayName;
        Email = master.Email;
        GradeText = BarberText.GradeText(master.Grade);
        RatingText = master.Rating is null
            ? Loc.T("Нет отзывов")
            : string.Concat(
                BarberText.Rating(master.Rating),
                " · ",
                BarberText.Reviews(master.ReviewsCount));
        StatusText = MasterStatus(master.IsActive, master.IsVerified);
        IsActive = master.IsActive;
        IsVerified = master.IsVerified;
        Initials = BarberText.Initials(master.DisplayName);
        AvatarImageUrl = string.IsNullOrWhiteSpace(avatarUrl)
            ? null
            : MediaUrl.Resolve(
                apiOptions.BaseAddress,
                MediaUrl.Resize(avatarUrl, "sm"));
        AvatarBadgeText = AvatarBadge(master.IsAvatarBlocked, master.AvatarModerationStatus);
    }

    /// <summary>
    /// Идентификатор профиля: по нему открывается карточка.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Имя мастера.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Email владельца профиля.
    /// </summary>
    public string Email { get; }

    /// <summary>
    /// Грейд по-русски.
    /// </summary>
    public string GradeText { get; }

    /// <summary>
    /// Рейтинг и число отзывов одной строкой; без отзывов — «Нет отзывов».
    /// </summary>
    public string RatingText { get; }

    /// <summary>
    /// Состояние профиля: «На проверке», «Подтверждён» или «Скрыт из поиска».
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Профиль виден в поиске.
    /// </summary>
    public bool IsActive { get; }

    /// <summary>
    /// Профиль подтверждён администратором.
    /// </summary>
    public bool IsVerified { get; }

    /// <summary>
    /// Инициалы для заглушки аватара.
    /// </summary>
    public string Initials { get; }

    /// <summary>
    /// Абсолютный адрес аватара; <c>null</c> — кадра нет.
    /// </summary>
    public string? AvatarImageUrl { get; }

    /// <summary>
    /// Есть ли аватар для показа картинкой.
    /// </summary>
    public bool HasAvatar => AvatarImageUrl is not null;

    /// <summary>
    /// Показывать ли инициалы вместо картинки.
    /// </summary>
    public bool ShowInitials => !HasAvatar;

    /// <summary>
    /// Бейдж состояния аватара: «Фото скрыто», «Отклонено» или «На проверке».
    /// </summary>
    public string? AvatarBadgeText { get; }

    /// <summary>
    /// Есть ли бейдж состояния аватара.
    /// </summary>
    public bool HasAvatarBadge => AvatarBadgeText is not null;

    /// <summary>
    /// Подпись состояния профиля — та же, что у бейджа строки.
    /// </summary>
    /// <param name="isActive">Профиль виден в поиске.</param>
    /// <param name="isVerified">Профиль подтверждён.</param>
    /// <returns>«Скрыт из поиска», «На проверке» или «Подтверждён».</returns>
    public static string MasterStatus(bool isActive, bool isVerified)
    {
        if (!isActive)
        {
            return Loc.T("Скрыт из поиска");
        }

        return isVerified ? Loc.T("Подтверждён") : Loc.T("На проверке");
    }

    /// <summary>
    /// Бейдж аватара по приоритету веба.
    /// </summary>
    /// <param name="isBlocked">Кадр скрыт администратором.</param>
    /// <param name="status">Состояние проверки кадра.</param>
    /// <returns>Текст бейджа либо <c>null</c>, если кадр одобрен.</returns>
    private static string? AvatarBadge(bool isBlocked, PhotoModerationStatus status)
    {
        if (isBlocked)
        {
            return Loc.T("Фото скрыто");
        }

        return status switch
        {
            PhotoModerationStatus.Rejected => Loc.T("Отклонено"),
            PhotoModerationStatus.Pending => Loc.T("На проверке"),
            _ => null,
        };
    }
}
