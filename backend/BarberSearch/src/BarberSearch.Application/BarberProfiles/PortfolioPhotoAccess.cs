using BarberSearch.Domain.Common;

namespace BarberSearch.Application.BarberProfiles;

/// <summary>
/// Минимальные данные фотографии для проверки права на раздачу файла: владелец профиля,
/// два флага видимости и состояние проверки.
/// </summary>
/// <remarks>
/// Читается по паре <c>(BarberProfileId, Url)</c> одним join'ом с профилем: раздача файла
/// проверяет видимость по БД на каждый запрос, а не доверяет одному URL. Публичной
/// видимостью считается <c>ModerationStatus == Approved &amp;&amp; IsPublished &amp;&amp;
/// !IsBlocked</c>; владелец и администратор получают файл при любом статусе.
/// </remarks>
/// <param name="OwnerUserId">Пользователь-владелец профиля.</param>
/// <param name="IsPublished">Опубликована ли фотография владельцем.</param>
/// <param name="IsBlocked">Заблокирована ли фотография администратором.</param>
/// <param name="ModerationStatus">Допуск фотографии к публикации.</param>
public sealed record PortfolioPhotoAccess(
    Guid OwnerUserId,
    bool IsPublished,
    bool IsBlocked,
    PhotoModerationStatus ModerationStatus);
