using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.AddUserFavorite;

/// <summary>
/// Команда добавления барбера в избранное пользователя.
/// </summary>
public record AddUserFavoriteCommand(Guid UserId, Guid BarberProfileId) : ICommand;
