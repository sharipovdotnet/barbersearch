using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.UnblockPortfolioPhoto;

/// <summary>
/// Команда снятия административной блокировки фотографии портфолио.
/// </summary>
/// <param name="BarberProfileId">Профиль, которому принадлежит фотография.</param>
/// <param name="PhotoId">Разблокируемая фотография.</param>
public record UnblockPortfolioPhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId) : ICommand;
