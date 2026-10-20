namespace BarberSearch.Contracts.Auth.Register;

/// <summary>
/// Данные самостоятельной регистрации.
/// </summary>
/// <remarks>
/// Роли здесь нет намеренно: публичная регистрация всегда создаёт <c>Customer</c>.
/// Пользователей с ролью <c>Admin</c> или <c>Barber</c> создаёт администратор через
/// <c>POST api/users</c>.
/// </remarks>
public record RegisterRequest(string Email, string Password, string FirstName, string LastName);
