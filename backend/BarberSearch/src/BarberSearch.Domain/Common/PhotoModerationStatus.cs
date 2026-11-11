namespace BarberSearch.Domain.Common;

/// <summary>
/// Состояние фотографии до/после проверки администратором. Один общий тип на оба места,
/// где появляется пользовательский кадр: работу в портфолио барбера и аватар пользователя.
/// </summary>
/// <remarks>
/// Это не видимость: <see cref="BarberSearch.Domain.PortfolioPhotos.PortfolioPhoto.IsPublished"/>
/// и <see cref="BarberSearch.Domain.PortfolioPhotos.PortfolioPhoto.IsBlocked"/> по-прежнему
/// означают намерение владельца и административную маску, а публичная выдача требует ещё и
/// <see cref="Approved"/>. У аватара пару составляют
/// <see cref="BarberSearch.Domain.Users.User.AvatarModerationStatus"/> и
/// <see cref="BarberSearch.Domain.Users.User.IsAvatarBlocked"/>.
/// <para>
/// Числовые значения стабильны для базы: они лежат в <c>smallint</c>-колонках
/// <c>portfolio_photos.moderation_status</c> и <c>users.avatar_moderation_status</c>
/// и закреплены check-ограничениями. Переименовать или перенумеровать значения нельзя
/// без миграции данных.
/// </para>
/// </remarks>
public enum PhotoModerationStatus
{
    /// <summary>
    /// Кадр загружен (или добавлен) и ждёт решения администратора; публично не показывается.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Кадр одобрен и публикуется по обычным правилам — с учётом
    /// <c>IsPublished</c>/<c>IsBlocked</c> у портфолио и <c>IsAvatarBlocked</c> у аватара.
    /// </summary>
    Approved = 2,

    /// <summary>
    /// Администратор отклонил кадр; причина лежит в <c>RejectionReason</c> /
    /// <c>AvatarRejectionReason</c>. Публично кадр не показывается.
    /// </summary>
    Rejected = 3
}
