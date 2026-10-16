using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.RemoveUserFavorite;

/// <summary>
/// Команда удаления барбера из избранного пользователя.
/// </summary>
public record RemoveUserFavoriteCommand(Guid UserId, Guid BarberProfileId) : ICommand;
