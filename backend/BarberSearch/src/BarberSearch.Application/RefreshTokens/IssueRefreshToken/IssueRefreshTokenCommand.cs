using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.RefreshTokens;

namespace BarberSearch.Application.RefreshTokens.IssueRefreshToken;

/// <summary>
/// Выпускает новую пару для пользователя: срок жизни задаёт вызывающий слой
/// (Web читает <c>Auth:RefreshTokenDays</c>).
/// </summary>
public record IssueRefreshTokenCommand(Guid UserId, int LifetimeDays) : ICommand;
