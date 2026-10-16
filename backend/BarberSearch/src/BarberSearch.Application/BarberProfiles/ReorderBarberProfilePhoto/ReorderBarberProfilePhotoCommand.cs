using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhoto;

/// <summary>
/// Команда изменения порядка фотографии в портфолио барбера.
/// </summary>
public record ReorderBarberProfilePhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId,
    int SortOrder) : ICommand;
