namespace BarberSearch.Contracts.Auth.EmailChange;

/// <summary>
/// Тело запроса подтверждения смены адреса по ссылке из письма.
/// </summary>
/// <remarks>
/// Токен nullable только в контракте: отсутствие токена — это ошибка формата
/// (<c>400 auth.emailChange.invalid</c>), а не ошибка разбора JSON. Новый адрес берётся из
/// строки токена, а не из запроса: иначе его можно было бы подменить в теле.
/// </remarks>
/// <param name="Token">Открытый токен из ссылки <c>/confirm-email-change?token=…</c>.</param>
public sealed record ConfirmEmailChangeRequest(string? Token);
