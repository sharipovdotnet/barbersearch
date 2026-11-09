using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PasswordResets.RequestPasswordReset;

/// <summary>
/// Запрос письма со ссылкой для сброса пароля.
/// </summary>
/// <remarks>
/// Команда анонимная и не опирается на <c>ICurrentUser</c>: идентичность берётся только
/// из адреса в теле запроса. Ответ не различает существующий и несуществующий адрес —
/// иначе перебором можно было бы выяснить, какие аккаунты есть в сервисе.
/// </remarks>
/// <param name="Email">Адрес из формы; нормализуется и проверяется в обработчике.</param>
public sealed record RequestPasswordResetCommand(string Email) : ICommand;
