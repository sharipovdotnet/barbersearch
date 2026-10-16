using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.UpdateUserProfile;

/// <summary>
/// Команда обновления профиля пользователя.
/// </summary>
public record UpdateUserProfileCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? AvatarUrl) : ICommand;
