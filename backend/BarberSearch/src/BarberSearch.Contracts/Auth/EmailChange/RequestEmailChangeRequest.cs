namespace BarberSearch.Contracts.Auth.EmailChange;

/// <summary>
/// Заявка на смену адреса электронной почты вошедшим пользователем.
/// </summary>
/// <remarks>
/// <para>
/// Пользователь берётся из claims, а не из тела: иначе любой мог бы менять чужой адрес.
/// Текущий пароль обязателен: без него украденная сессия увела бы аккаунт на чужой ящик.
/// Поля nullable только в контракте — пустые значения дают бизнес-коды вроде
/// <c>400 user.changeEmail.newEmail</c>, а не ошибку разбора JSON.
/// </para>
/// <para>
/// Новый адрес не применяется сразу: на него уходит ссылка подтверждения, а на текущий —
/// уведомление. Ответ <c>204</c> означает только «письма поставлены в outbox».
/// </para>
/// </remarks>
/// <param name="NewEmail">Запрошенный новый адрес.</param>
/// <param name="CurrentPassword">Текущий пароль для подтверждения личности.</param>
public sealed record RequestEmailChangeRequest(string? NewEmail, string? CurrentPassword);
