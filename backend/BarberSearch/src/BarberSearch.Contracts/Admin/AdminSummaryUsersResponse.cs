namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Пользователи без подтверждения email в админской сводке.
/// </summary>
/// <remarks>
/// <see cref="UnverifiedCount"/> — ровно фильтр <c>AdminUserStatus.Unverified</c> при
/// <c>role=all</c>: <c>is_email_verified = false</c> у всех ролей, включая заблокированных.
/// Это не «все пользователи»: добавление роли или условия <c>is_active</c> разошлось бы
/// с очередью раздела.
/// </remarks>
/// <param name="UnverifiedCount">Сколько пользователей не подтвердило email.</param>
public record AdminSummaryUsersResponse(int UnverifiedCount);
