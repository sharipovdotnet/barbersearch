using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;

namespace BarberSearch.Application.Users.Admin.GetAdminUsers;

/// <summary>
/// Запрос админского списка пользователей.
/// </summary>
/// <param name="Status">Фильтр статуса: <c>all</c>, <c>active</c>, <c>blocked</c> или <c>unverified</c>.</param>
/// <param name="Role">Фильтр роли: <c>all</c> либо имя роли (<c>Customer</c>, <c>Barber</c>, <c>Admin</c>).</param>
/// <param name="Search">Поиск по имени, фамилии или email.</param>
/// <param name="Cursor">Непрозрачный курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
public record GetAdminUsersQuery(
    string? Status,
    string? Role,
    string? Search,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminUserResponse>>;
