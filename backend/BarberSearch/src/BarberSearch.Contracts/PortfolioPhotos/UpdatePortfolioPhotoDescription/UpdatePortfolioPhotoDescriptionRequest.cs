namespace BarberSearch.Contracts.PortfolioPhotos.UpdatePortfolioPhotoDescription;

/// <summary>
/// Данные запроса на изменение подписи к фотографии портфолио.
/// </summary>
/// <remarks>
/// <see cref="Description"/> = <c>null</c> означает «убрать подпись» — это осознанное
/// значение, а не «поле не пришло».
/// </remarks>
public record UpdatePortfolioPhotoDescriptionRequest(string? Description);
