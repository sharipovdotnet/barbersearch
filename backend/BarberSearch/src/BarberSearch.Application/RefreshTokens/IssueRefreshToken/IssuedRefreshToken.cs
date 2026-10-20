namespace BarberSearch.Application.RefreshTokens.IssueRefreshToken;

/// <summary>
/// Выданный refresh-токен. Открытое значение возвращается клиенту один раз и в базе не хранится.
/// </summary>
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);
