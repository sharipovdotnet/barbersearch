namespace BarberSearch.Application.BarberProfiles;

/// <summary>
/// Минимальные данные фотографии для проверки права на раздачу файла: владелец профиля
/// и два флага видимости.
/// </summary>
/// <remarks>
/// Читается по паре <c>(BarberProfileId, Url)</c> одним join'ом с профилем: раздача файла
/// проверяет видимость по БД на каждый запрос, а не доверяет одному URL. Публичной
/// видимостью считается <c>IsPublished &amp;&amp; !IsBlocked</c>.
/// </remarks>
/// <param name="OwnerUserId">Пользователь-владелец профиля.</param>
/// <param name="IsPublished">Опубликована ли фотография владельцем.</param>
/// <param name="IsBlocked">Заблокирована ли фотография администратором.</param>
public sealed record PortfolioPhotoAccess(
    Guid OwnerUserId,
    bool IsPublished,
    bool IsBlocked);
