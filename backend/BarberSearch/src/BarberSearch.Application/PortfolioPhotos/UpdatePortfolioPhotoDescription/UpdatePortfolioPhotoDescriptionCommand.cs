using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.UpdatePortfolioPhotoDescription;

/// <summary>
/// Команда изменения подписи к фотографии портфолио.
/// </summary>
/// <remarks>
/// <c>null</c> в <c>Description</c> означает «убрать подпись».
/// </remarks>
public record UpdatePortfolioPhotoDescriptionCommand(
    Guid BarberProfileId,
    Guid PhotoId,
    string? Description) : ICommand;
