namespace BarberSearch.Domain.Users;

/// <summary>
/// Роль пользователя. Пользователь с ролью <see cref="Barber"/> дополнительно имеет
/// профиль барбера (<c>BarberProfile</c>).
/// </summary>
public enum UserRole
{
    Customer = 1,
    Barber = 2,
    Admin = 3
}
