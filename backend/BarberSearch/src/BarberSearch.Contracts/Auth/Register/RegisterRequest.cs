namespace BarberSearch.Contracts.Auth.Register;

/// <summary>
/// Данные самостоятельной регистрации.
/// </summary>
/// <remarks>
/// Роли здесь нет намеренно: публичная регистрация всегда создаёт <c>Customer</c>.
/// Пользователей с ролью <c>Admin</c> или <c>Barber</c> создаёт администратор через
/// <c>POST api/users</c>.
/// <para>
/// <c>ConsentVersion</c> — редакция Политики, которую подтвердил человек. Поле nullable
/// в контракте только потому, что отсутствие согласия — это валидационная ошибка
/// (<c>auth.register.consentRequired</c>), а не ошибка разбора тела; по смыслу оно
/// обязательно. Время и IP сервер ставит сам.
/// </para>
/// </remarks>
public record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? ConsentVersion = null);
