using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// HTTP-клиент админской модерации отзывов.
/// </summary>
/// <remarks>
/// <para>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: роль администратора сервер берёт из
/// токена, в запросе её нет. Список читается теми же параметрами, что у веба
/// (<c>status</c>, <c>cursor</c>, <c>pageSize</c>), карточка — по идентификатору.
/// </para>
/// <para>
/// Публикация и скрытие — ручки записи самого отзыва, а не админского контроллера: они уже
/// закрыты политикой <c>AdminOnly</c> и отвечают <c>204</c>. Тело запроса не отправляется,
/// поэтому успех — это код ответа, а не разобранный JSON.
/// </para>
/// </remarks>
public sealed class AdminReviewsApi : IAdminReviewsApi
{
    private const string ReviewsPath = "api/admin/reviews";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AdminReviewsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<AdminReviewResponse>> GetReviewsAsync(
        AdminReviewStatusFilter status,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildReviewsPath(status, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminReviewResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty admin reviews response.");
    }

    /// <inheritdoc />
    public async Task<AdminReviewResponse> GetReviewAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildReviewPath(reviewId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var review = await response.Content
            .ReadFromJsonAsync<AdminReviewResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return review ?? throw new InvalidOperationException("The API returned an empty admin review response.");
    }

    /// <inheritdoc />
    public async Task PublishReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsync(BuildPublicationPath(reviewId), content: null, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task HideReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .DeleteAsync(BuildPublicationPath(reviewId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Собирает адрес страницы очереди.
    /// </summary>
    /// <remarks>
    /// Курсор непрозрачен и кодируется целиком: он может содержать символы, недопустимые
    /// в адресе. Пустой курсор не передаётся вовсе — отсутствие параметра и есть первая
    /// страница, а пустая строка была бы невалидным курсором.
    /// </remarks>
    private static string BuildReviewsPath(
        AdminReviewStatusFilter status,
        string? cursor,
        int pageSize)
    {
        var path = string.Concat(
            ReviewsPath,
            "?status=",
            StatusValue(status),
            "&pageSize=",
            pageSize.ToString(CultureInfo.InvariantCulture));

        return string.IsNullOrWhiteSpace(cursor)
            ? path
            : string.Concat(path, "&cursor=", Uri.EscapeDataString(cursor));
    }

    /// <summary>
    /// Собирает адрес одного отзыва.
    /// </summary>
    private static string BuildReviewPath(Guid reviewId) => string.Concat(
        ReviewsPath,
        "/",
        reviewId.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>
    /// Собирает адрес публикации: один и тот же путь у <c>POST</c> и <c>DELETE</c>.
    /// </summary>
    private static string BuildPublicationPath(Guid reviewId) => string.Concat(
        "api/reviews/",
        reviewId.ToString("D", CultureInfo.InvariantCulture),
        "/publication");

    /// <summary>
    /// Переводит фильтр в строку, которую понимает сервер.
    /// </summary>
    private static string StatusValue(AdminReviewStatusFilter status) => status switch
    {
        AdminReviewStatusFilter.Pending => "pending",
        AdminReviewStatusFilter.Published => "published",
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "Unknown admin review status."),
    };
}
