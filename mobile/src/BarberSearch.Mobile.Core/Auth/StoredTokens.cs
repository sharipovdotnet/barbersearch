namespace BarberSearch.Mobile.Core.Auth;

/// <summary>
/// Пара токенов, которую приложение хранит между запусками.
/// </summary>
/// <remarks>
/// Хранится одной записью, а не двумя: между двумя записями приложение могло бы упасть
/// и оставить новую половинку рядом со старой — сервер счёл бы предъявленный refresh
/// повторно использованным и отозвал всю цепочку.
/// </remarks>
/// <param name="AccessToken">Короткоживущий токен для заголовка <c>Authorization</c>.</param>
/// <param name="RefreshToken">Долгоживущий токен для обмена на новую пару.</param>
public sealed record StoredTokens(string AccessToken, string RefreshToken);
