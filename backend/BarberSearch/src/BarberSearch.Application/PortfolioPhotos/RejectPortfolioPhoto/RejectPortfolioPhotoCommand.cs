using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PortfolioPhotos.RejectPortfolioPhoto;

/// <summary>
/// Команда отклонения фотографии портфолио, ждущей проверки.
/// </summary>
/// <remarks>
/// Причина обязательна: она показывается владельцу профиля, чтобы он понял, что не так.
/// Домен тримит её и ограничивает 500 символами:
/// <c>400 portfolioPhoto.moderation.reasonRequired</c> / <c>.reasonTooLong</c> с
/// <c>invalidField = "reason"</c>. Повторное решение — <c>409
/// portfolioPhoto.moderation.invalidStatus</c>; фотографии нет в этом профиле —
/// <c>404 portfolioPhoto.moderation.notFound</c>.
/// </remarks>
/// <param name="BarberProfileId">Профиль, которому принадлежит фотография.</param>
/// <param name="PhotoId">Отклоняемая фотография.</param>
/// <param name="Reason">Причина отказа; обрезается доменом по краям.</param>
public record RejectPortfolioPhotoCommand(
    Guid BarberProfileId,
    Guid PhotoId,
    string? Reason) : ICommand;
