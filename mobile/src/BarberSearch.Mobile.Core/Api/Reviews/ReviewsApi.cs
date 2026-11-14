using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.CreateReview;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;

namespace BarberSearch.Mobile.Core.Api.Reviews;

/// <summary>
/// HTTP-клиент отзывов: создание, «мои отзывы», правка и удаление.
/// </summary>
/// <remarks>
/// Тело собирается из контрактов бэкенда, чтобы расхождение ломало сборку, а не отправку.
/// Ответ <c>201</c> несёт только идентификатор: остальные поля отзыва экрану не нужны, он
/// закрывается сразу после успеха. <c>204</c> у удаления тело не читает — успех это статус.
/// </remarks>
public sealed class ReviewsApi : IReviewsApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public ReviewsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateReviewAsync(
        Guid bookingId,
        int rating,
        string? text,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateReviewRequest(bookingId, rating, text);

        using var response = await _httpClient
            .PostAsJsonAsync("api/reviews", request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var created = await response.Content
            .ReadFromJsonAsync<CreatedResourceResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Идентификатор генерирует домен: пустой Guid означает нарушение контракта сервером.
        return created is null || created.Id == Guid.Empty
            ? throw new InvalidOperationException("The API returned an empty review response.")
            : created.Id;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<MyReviewResponse>> GetMyReviewsAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildMyReviewsPath(cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<MyReviewResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty reviews response.");
    }

    /// <inheritdoc />
    public async Task<UpdateReviewResponse> UpdateReviewAsync(
        Guid reviewId,
        int rating,
        string? text,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateReviewRequest(rating, text);

        using var response = await _httpClient
            .PutAsJsonAsync(BuildReviewPath(reviewId), request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var updated = await response.Content
            .ReadFromJsonAsync<UpdateReviewResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return updated ?? throw new InvalidOperationException("The API returned an empty review update response.");
    }

    /// <inheritdoc />
    public async Task DeleteReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .DeleteAsync(BuildReviewPath(reviewId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Собирает адрес списка своих отзывов.
    /// </summary>
    /// <remarks>
    /// Пустой курсор не передаётся вовсе: сервер трактует отсутствие параметра как первую
    /// страницу, а пустая строка была бы невалидным курсором. Курсор кодируется целиком:
    /// он непрозрачен и может содержать символы, недопустимые в адресе.
    /// </remarks>
    private static string BuildMyReviewsPath(string? cursor, int pageSize)
    {
        var path = string.Concat(
            "api/reviews/mine?pageSize=",
            pageSize.ToString(CultureInfo.InvariantCulture));

        return string.IsNullOrWhiteSpace(cursor)
            ? path
            : string.Concat(path, "&cursor=", Uri.EscapeDataString(cursor));
    }

    /// <summary>
    /// Собирает адрес одного отзыва.
    /// </summary>
    private static string BuildReviewPath(Guid reviewId) => string.Concat(
        "api/reviews/",
        reviewId.ToString("D", CultureInfo.InvariantCulture));
}
