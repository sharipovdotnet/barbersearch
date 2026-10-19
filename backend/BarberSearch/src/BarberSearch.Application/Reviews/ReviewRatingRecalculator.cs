using BarberSearch.Application.BarberProfiles;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Reviews;

/// <summary>
/// Пересчитывает денормализованные <c>BarberProfile.Rating</c> и <c>BarberProfile.ReviewsCount</c>
/// по опубликованным отзывам.
/// </summary>
/// <remarks>
/// <para>
/// Вызывается только внутри <see cref="Abstractions.IUnitOfWork"/>: сначала берётся row lock
/// на строку <c>barber_profiles</c> (<c>SELECT … FOR UPDATE</c>), затем читается сводка.
/// Без блокировки два параллельных <c>Publish</c>/<c>Hide</c> прочитали бы один и тот же
/// старый набор отзывов и потеряли бы обновление (lost update).
/// </para>
/// <para>
/// Округление шкалы 1.00..5.00 и правило «<c>null</c> при нуле отзывов» остаются в домене:
/// <see cref="BarberSearch.Domain.BarberProfiles.BarberProfile.UpdateRating"/> — единственное
/// место, где меняются оба поля. Здесь только читаются сводка и корень профиля.
/// </para>
/// </remarks>
public sealed class ReviewRatingRecalculator
{
    private readonly IBarberProfileRepository _barberProfileRepository;
    private readonly IReviewRepository _reviewRepository;

    public ReviewRatingRecalculator(
        IBarberProfileRepository barberProfileRepository,
        IReviewRepository reviewRepository)
    {
        ArgumentNullException.ThrowIfNull(barberProfileRepository);
        ArgumentNullException.ThrowIfNull(reviewRepository);

        _barberProfileRepository = barberProfileRepository;
        _reviewRepository = reviewRepository;
    }

    /// <summary>
    /// Блокирует профиль, пересчитывает сводку и сохраняет новый рейтинг.
    /// </summary>
    /// <param name="barberProfileId">Профиль, чьи отзывы изменились.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// <c>Failure</c> при отсутствии профиля, ошибке чтения сводки, нарушении доменного
    /// инварианта или ошибке сохранения. Транзакцию откатывает вызывающий
    /// <c>IUnitOfWork</c>.
    /// </returns>
    public async Task<UnitResult<Failure>> RecalculateAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken)
    {
        // Блокировка обязана стоять до чтения сводки: иначе два параллельных пересчёта
        // увидят одно и то же состояние отзывов и второй перезапишет первый.
        var profileResult = await _barberProfileRepository.GetForRatingUpdateAsync(
            barberProfileId,
            cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var summaryResult = await _reviewRepository.GetRatingSummaryAsync(
            barberProfileId,
            cancellationToken);
        if (summaryResult.IsFailure)
        {
            return summaryResult.Error;
        }

        var summary = summaryResult.Value;
        var profile = profileResult.Value;

        var ratingResult = profile.UpdateRating(summary.AverageRating, summary.Count);
        if (ratingResult.IsFailure)
        {
            return ratingResult.Error;
        }

        return await _barberProfileRepository.UpdateAsync(profile, cancellationToken);
    }
}
