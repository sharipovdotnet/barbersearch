namespace BarberSearch.Contracts.Auth.PasswordReset;

/// <summary>
/// Тело запроса ссылки для сброса пароля.
/// </summary>
/// <remarks>
/// Действие анонимное и не использует текущую сессию: адрес приходит в теле, даже если
/// у клиента есть cookie. <c>Email</c> nullable только в контракте — пустое или кривое
/// значение не ошибка разбора JSON, а ситуация, на которую сервер отвечает тем же
/// <c>204</c>, что и для существующего адреса (анти-энумерация).
/// </remarks>
/// <param name="Email">Адрес аккаунта, на который нужно отправить ссылку.</param>
public sealed record RequestPasswordResetRequest(string? Email);
