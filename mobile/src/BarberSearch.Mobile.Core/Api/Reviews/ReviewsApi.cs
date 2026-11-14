using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.CreateReview;

namespace BarberSearch.Mobile.Core.Api.Reviews;

/// <summary>
/// HTTP-клиент отзывов: <c>POST api/reviews</c>.
/// </summary>
/// <remarks>
/// Тело собирается из контракта бэкенда, чтобы расхождение ломало сборку, а не отправку.
/// Ответ <c>201</c> несёт только идентификатор: остальные поля отзыва экрану не нужны, он
/// закрывается сразу после успеха.
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
}
