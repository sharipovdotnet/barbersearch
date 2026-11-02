using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Users.Me;

namespace BarberSearch.Application.Users.GetMyProfile;

/// <summary>
/// Запрос профиля текущего пользователя.
/// </summary>
/// <remarks>
/// Параметра <c>userId</c> нет намеренно: профиль всегда принадлежит вызывающему.
/// Идентификатор берётся из claim'а <c>sub</c> в обработчике.
/// </remarks>
public record GetMyProfileQuery : IQuery<MyProfileResponse>;
