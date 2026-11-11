using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.ApprovePortfolioPhoto;

/// <summary>
/// Команда одобрения фотографии портфолио, ждущей проверки.
/// </summary>
/// <remarks>
/// Одобрение — допуск к публикации, а не публикация: кадр появится публично по обычным
/// правилам владельца (<c>IsPublished</c>) и с учётом административной блокировки.
/// Повторное решение по уже проверенному кадру — <c>409
/// portfolioPhoto.moderation.invalidStatus</c>; фотографии нет в этом профиле —
/// <c>404 portfolioPhoto.moderation.notFound</c>. Оба кода возвращает домен или проверка
/// коллекции агрегата.
/// </remarks>
/// <param name="BarberProfileId">Профиль, которому принадлежит фотография.</param>
/// <param name="PhotoId">Одобряемая фотография.</param>
public record ApprovePortfolioPhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId) : ICommand;
