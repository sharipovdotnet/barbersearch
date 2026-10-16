using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.PublishPortfolioPhoto;

/// <summary>
/// Команда публикации фотографии портфолио.
/// </summary>
public record PublishPortfolioPhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId) : ICommand;
