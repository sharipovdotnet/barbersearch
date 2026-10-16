using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.UnpublishPortfolioPhoto;

/// <summary>
/// Команда снятия фотографии портфолио с публикации.
/// </summary>
public record UnpublishPortfolioPhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId) : ICommand;
