using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfilePhoto;

/// <summary>
/// Команда удаления фотографии из портфолио барбера.
/// </summary>
/// <remarks>
/// Идентификатор профиля нужен, чтобы загрузить агрегат: дочерние сущности правятся только через него.
/// </remarks>
public record RemoveBarberProfilePhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId) : ICommand;
