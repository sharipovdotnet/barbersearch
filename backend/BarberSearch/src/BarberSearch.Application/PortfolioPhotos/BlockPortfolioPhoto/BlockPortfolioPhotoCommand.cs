using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.BlockPortfolioPhoto;

/// <summary>
/// Команда административной блокировки фотографии портфолио.
/// </summary>
/// <remarks>
/// Блокировка скрывает фотографию из публичной карточки и запрещает её раздачу по URL.
/// Снять её владелец профиля не может — только администратор.
/// </remarks>
/// <param name="BarberProfileId">Профиль, которому принадлежит фотография.</param>
/// <param name="PhotoId">Блокируемая фотография.</param>
public record BlockPortfolioPhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId) : ICommand;
