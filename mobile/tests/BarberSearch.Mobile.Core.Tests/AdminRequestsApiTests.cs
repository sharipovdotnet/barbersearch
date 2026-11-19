using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.BarberRequests.RejectBarberRequest;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента администраторских заявок: список с фильтром и курсором, карточка,
/// одобрение и отказ с причиной.
/// </summary>
/// <remarks>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест не поднимает бэкенд, но проходит
/// через настоящий <see cref="HttpClient"/> и настоящие настройки JSON. Курсор непрозрачен,
/// поэтому адрес проверяется и как экранированная строка, и после декодирования. Тела
/// успешных решений клиент не читает: экран уходит назад и перечитывает очередь.
/// </remarks>
public sealed class AdminRequestsApiTests
{
    private static readonly Guid RequestId = Guid.Parse("9e200000-0000-4000-8000-0000000000a1");
    private static readonly Guid ApplicantId = Guid.Parse("9e200000-0000-4000-8000-0000000000a2");

    [Fact]
    public async Task GetRequestsAsync_PendingFirstPage_SendsStatusAndPageSizeWithoutCursor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RequestsPageJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        await api.GetRequestsAsync(AdminRequestListFilter.Pending, cursor: null, pageSize: 20);

        var uri = handler.LastRequestUri!;

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/admin/barber-requests", uri.AbsolutePath);
        Assert.Contains("status=pending", uri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=20", uri.Query, StringComparison.Ordinal);

        // Отсутствие курсора — это первая страница; пустая строка была бы невалидным курсором.
        Assert.DoesNotContain("cursor=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRequestsAsync_AllWithCursor_IsUrlEscaped()
    {
        const string cursor = "abc+/=def";

        var handler = new StubHandler(HttpStatusCode.OK, RequestsPageJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        await api.GetRequestsAsync(AdminRequestListFilter.All, cursor, pageSize: 10);

        var query = handler.LastRequestUri!.Query;

        Assert.Contains("status=all", query, StringComparison.Ordinal);
        Assert.Contains("pageSize=10", query, StringComparison.Ordinal);

        var cursorParameter = query
            .Split('&')
            .Single(part => part.StartsWith("cursor=", StringComparison.Ordinal));
        var escaped = cursorParameter["cursor=".Length..];

        // Курсор может содержать символы, недопустимые в адресе: «+», «/» и «=» обязаны
        // уехать экранированными, иначе сервер получит другой курсор.
        Assert.Equal("abc%2B%2F%3Ddef", escaped);
        Assert.Equal(cursor, WebUtility.UrlDecode(escaped));
    }

    [Fact]
    public async Task GetRequestsAsync_SingleRequest_ParsesApplicantAndStatus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RequestsPageJson(nextCursor: "\"next-page\""));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        var page = await api.GetRequestsAsync(AdminRequestListFilter.Pending, cursor: null, pageSize: 20);

        var item = Assert.Single(page.Items);

        // Админский контракт несёт заявителя: без него строку нельзя опознать по человеку.
        Assert.Equal(RequestId, item.Id);
        Assert.Equal("Игорь Мастер", item.DisplayName);
        Assert.Equal(5, item.YearsOfExperience);
        Assert.Equal("Москва", item.City);
        Assert.True(item.HasSalon);
        Assert.Equal(ApplicantId, item.Applicant.Id);
        Assert.Equal("Игорь Иванов", item.Applicant.FullName);
        Assert.Equal("igor@example.com", item.Applicant.Email);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), item.CreatedAt);
        Assert.Null(item.ReviewedAt);

        Assert.Equal("next-page", page.NextCursor);
    }

    [Fact]
    public async Task GetRequestAsync_ById_ParsesRejectionReasonAndReviewer()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RejectedRequestJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        var request = await api.GetRequestAsync(RequestId);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal($"/api/admin/barber-requests/{RequestId:D}", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(BarberRequestStatus.Rejected, request.Status);
        Assert.Equal("Мало опыта", request.RejectionReason);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 12, 30, 0, TimeSpan.Zero), request.ReviewedAt);
    }

    [Fact]
    public async Task ApproveRequestAsync_Success_PostsWithoutBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RequestsPageJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        await api.ApproveRequestAsync(RequestId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal($"/api/admin/barber-requests/{RequestId:D}/approval", handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task RejectRequestAsync_WithReason_SendsReasonJson()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RequestsPageJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        await api.RejectRequestAsync(RequestId, "Мало опыта");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal($"/api/admin/barber-requests/{RequestId:D}/rejection", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal("Мало опыта", root.GetProperty("reason").GetString());

        var parsed = JsonSerializer.Deserialize<RejectBarberRequestRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Мало опыта", parsed.Reason);
    }

    [Fact]
    public async Task RejectRequestAsync_ReasonTooLong_ThrowsWithCode()
    {
        const string problemJson = """
            {
              "title": "Bad Request",
              "status": 400,
              "detail": "A rejection reason cannot be longer than 500 characters.",
              "errorCodes": ["barberRequest.rejection.reasonTooLong"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.RejectRequestAsync(RequestId, new string('а', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);

        // По этому коду карточка говорит, что причина длиннее предела, а не «не удалось».
        Assert.Contains("barberRequest.rejection.reasonTooLong", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ApproveRequestAsync_AlreadyReviewed_ThrowsWithStatusAndCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The request has already been reviewed.",
              "errorCodes": ["barberRequest.review.invalidStatus"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.ApproveRequestAsync(RequestId));

        // Второе решение с другого устройства: экран должен сказать «уже рассмотрели».
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("barberRequest.review.invalidStatus", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetRequestAsync_NotFound_ThrowsWithStatusAndCode()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "errorCodes": ["barberRequest.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AdminRequestsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetRequestAsync(RequestId));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("barberRequest.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    /// <summary>
    /// Ответ списка: одна ожидающая заявка с заявителем.
    /// </summary>
    /// <param name="nextCursor">JSON-значение курсора следующей страницы: строка либо <c>null</c>.</param>
    private static string RequestsPageJson(string nextCursor = "null") => $$"""
        {
          "items": [
            {
              "id": "{{RequestId:D}}",
              "status": "Pending",
              "displayName": "Игорь Мастер",
              "yearsOfExperience": 5,
              "city": "Москва",
              "about": "Стригу с 2018 года.",
              "hasSalon": true,
              "createdAt": "2026-11-15T10:00:00+00:00",
              "reviewedAt": null,
              "rejectionReason": null,
              "reviewedByUserId": null,
              "applicant": {
                "id": "{{ApplicantId:D}}",
                "fullName": "Игорь Иванов",
                "email": "igor@example.com"
              }
            }
          ],
          "nextCursor": {{nextCursor}}
        }
        """;

    /// <summary>
    /// Ответ карточки: отклонённая заявка с причиной и автором решения.
    /// </summary>
    private static string RejectedRequestJson() => $$"""
        {
          "id": "{{RequestId:D}}",
          "status": "Rejected",
          "displayName": "Игорь Мастер",
          "yearsOfExperience": 5,
          "city": "Москва",
          "about": null,
          "hasSalon": false,
          "createdAt": "2026-11-15T10:00:00+00:00",
          "reviewedAt": "2026-11-16T12:30:00+00:00",
          "rejectionReason": "Мало опыта",
          "reviewedByUserId": "{{ApplicantId:D}}",
          "applicant": {
            "id": "{{ApplicantId:D}}",
            "fullName": "Игорь Иванов",
            "email": "igor@example.com"
          }
        }
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(HttpStatusCode statusCode, string? body = null, string contentType = "application/json")
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            };
        }
    }
}
