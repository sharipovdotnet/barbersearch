using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.LinkUserBarberProfile;

/// <summary>
/// Команда привязки профиля барбера к пользователю.
/// </summary>
public record LinkUserBarberProfileCommand(Guid UserId, Guid BarberProfileId) : ICommand;
