using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.UpdateBarberProfileRating;

/// <summary>
/// Команда пересчёта рейтинга барбера по отзывам.
/// </summary>
/// <remarks>
/// <see cref="Rating"/> = <c>null</c> вместе с <see cref="ReviewsCount"/> = 0 означает «отзывов нет».
/// Согласованность пары значений проверяет домен.
/// </remarks>
public record UpdateBarberProfileRatingCommand(
    Guid BarberProfileId,
    decimal? Rating,
    int ReviewsCount) : ICommand;
