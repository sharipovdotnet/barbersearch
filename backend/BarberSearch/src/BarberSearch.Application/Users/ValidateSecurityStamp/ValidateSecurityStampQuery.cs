using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.ValidateSecurityStamp;

/// <summary>
/// Запрос проверки security stamp: отпечаток из токена против сохранённого у пользователя.
/// </summary>
/// <remarks>
/// Проверка живёт в Application, а не в Web, чтобы её можно было покрыть юнит-тестами
/// без тестового проекта Web (R-11). Web-адаптер только парсит claims и вызывает этот запрос.
/// Ответ — <see langword="true"/>, только если пользователь существует, активен и отпечатки
/// совпадают побайтово (ordinal).
/// </remarks>
/// <param name="UserId">Идентификатор пользователя из claim <c>sub</c>.</param>
/// <param name="SecurityStamp">Отпечаток из claim <c>security_stamp</c> выданного токена.</param>
public record ValidateSecurityStampQuery(
    Guid UserId,
    string SecurityStamp) : IQuery<bool>;
