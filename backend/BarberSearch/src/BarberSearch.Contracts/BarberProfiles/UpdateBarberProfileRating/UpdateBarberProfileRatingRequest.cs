namespace BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileRating;

/// <summary>
/// Данные запроса на пересчёт рейтинга барбера по отзывам.
/// </summary>
/// <remarks>
/// <see cref="Rating"/> = <c>null</c> вместе с <see cref="ReviewsCount"/> = 0 означает
/// «отзывов нет». Согласованность пары значений проверяет домен, поэтому запрос
/// не пытается их «починить».
/// </remarks>
public record UpdateBarberProfileRatingRequest(
    decimal? Rating,
    int ReviewsCount);
