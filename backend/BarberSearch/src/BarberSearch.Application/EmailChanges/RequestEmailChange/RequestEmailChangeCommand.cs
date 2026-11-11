using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailChanges.RequestEmailChange;

/// <summary>
/// Заявка на смену адреса электронной почты с подтверждением по ссылке.
/// </summary>
/// <remarks>
/// Пользователь берётся из claims контроллера, а не из тела: иначе любой мог бы менять чужой
/// адрес. Текущий пароль обязателен — без него украденная сессия увела бы аккаунт на чужой
/// ящик. Сам адрес меняется только после перехода по ссылке из письма.
/// </remarks>
/// <param name="UserId">Пользователь, который запрашивает смену адреса.</param>
/// <param name="NewEmail">Запрошенный новый адрес; нормализуется в обработчике.</param>
/// <param name="CurrentPassword">Текущий пароль для подтверждения личности.</param>
public sealed record RequestEmailChangeCommand(
    Guid UserId,
    string NewEmail,
    string CurrentPassword) : ICommand;
