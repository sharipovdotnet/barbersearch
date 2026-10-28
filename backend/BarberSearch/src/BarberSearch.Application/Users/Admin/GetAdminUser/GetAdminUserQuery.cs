using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Users.Admin;

namespace BarberSearch.Application.Users.Admin.GetAdminUser;

/// <summary>
/// Запрос одного пользователя для админской карточки.
/// </summary>
/// <param name="UserId">Идентификатор пользователя.</param>
public record GetAdminUserQuery(Guid UserId) : IQuery<AdminUserDetailResponse>;
