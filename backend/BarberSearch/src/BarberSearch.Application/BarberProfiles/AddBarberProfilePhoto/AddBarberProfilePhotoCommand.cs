using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfilePhoto;

/// <summary>
/// Команда добавления фотографии в портфолио барбера.
/// </summary>
/// <remarks>
/// <see cref="ThumbnailUrl"/> и <see cref="Description"/> необязательны.
/// </remarks>
public record AddBarberProfilePhotoCommand(
    Guid BarberProfileId,
    string Url,
    string? ThumbnailUrl,
    string? Description) : ICommand;
