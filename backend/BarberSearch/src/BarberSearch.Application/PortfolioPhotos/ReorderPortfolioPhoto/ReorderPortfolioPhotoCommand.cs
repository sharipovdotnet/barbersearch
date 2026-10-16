using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.ReorderPortfolioPhoto;

/// <summary>
/// Команда изменения порядка вывода фотографии в портфолио.
/// </summary>
public record ReorderPortfolioPhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId,
    int SortOrder) : ICommand;
