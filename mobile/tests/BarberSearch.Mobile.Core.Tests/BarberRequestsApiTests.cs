using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.BarberRequests;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки HTTP-клиента заявки на роль мастера: чтение своей заявки и её создание.
/// </summary>
/// <remarks>
/// <para>
/// Сеть подменяется <see cref="HttpMessageHandler"/>, но запрос идёт через настоящий
/// <see cref="HttpClient"/> и настоящие настройки JSON: тест ловит и ошибку адреса, и
/// расхождение имён полей контракта, и потерю <c>null</c> при разборе.
/// </para>
/// <para>
/// Тело <c>POST</c> проверяется по составу, а не только по значениям: лишнее поле клиент
/// отправил бы серверу, который его не ждёт, а пропавшее осталось бы пустым. Ошибки
/// разбираются по коду состояния и публичным кодам: английский <c>detail</c> экрану не нужен.
/// </para>
/// </remarks>
public sealed class BarberRequestsApiTests
{
    private const string RequestId = "0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f70";

    /// <summary>
    /// Ровно те поля, которые клиент имеет право отправить в <c>POST</c>: лишнее сервер
    /// не ждёт, а пропавшее осталось бы у заявки пустым.
    /// </summary>
    private static readonly string[] ContractFieldNames =
    [
        "about",
        "city",
        "displayName",
        "hasSalon",
        "yearsOfExperience",
    ];

    /// <summary>
    /// Английские тексты валидации сервера: экран их не показывает, но клиент обязан
    /// донести их до вызывающего кода, а не потерять при разборе.
    /// </summary>
    private static readonly string[] DisplayNameValidationErrors = ["The display name is required."];

    private static readonly string[] CityValidationErrors = ["The city is too long."];

    /// <summary>
    /// Заявка в ответе <c>GET mine</c> разбирается целиком: каждое поле карточки статуса
    /// живёт в своём свойстве ответа.
    /// </summary>
    [Fact]
    public async Task GetMineAsync_RequestPresent_ParsesEveryField()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MineJson(RequestJson(
            status: "Rejected",
            about: "\"Стригу с 2019 года.\"",
            hasSalon: "true",
            reviewedAt: "\"2026-11-05T09:15:00+00:00\"",
            rejectionReason: "\"Не хватает портфолио.\"")));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        var mine = await api.GetMineAsync();

        Assert.NotNull(mine.Request);
        Assert.Equal(Guid.Parse(RequestId), mine.Request.Id);
        Assert.Equal(BarberRequestStatus.Rejected, mine.Request.Status);
        Assert.Equal("Ева Мастерова", mine.Request.DisplayName);
        Assert.Equal(7, mine.Request.YearsOfExperience);
        Assert.Equal("Екатеринбург", mine.Request.City);
        Assert.Equal("Стригу с 2019 года.", mine.Request.About);
        Assert.True(mine.Request.HasSalon);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 30, 0, TimeSpan.Zero), mine.Request.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 9, 15, 0, TimeSpan.Zero), mine.Request.ReviewedAt);
        Assert.Equal("Не хватает портфолио.", mine.Request.RejectionReason);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/barber-requests/mine", handler.LastRequestUri!.AbsolutePath);
    }

    /// <summary>
    /// Заявки может не быть: <c>request: null</c> — штатное состояние формы, а не ошибка.
    /// </summary>
    [Fact]
    public async Task GetMineAsync_NoRequest_ReturnsNullRequest()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"request":null}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        var mine = await api.GetMineAsync();

        Assert.Null(mine.Request);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/barber-requests/mine", handler.LastRequestUri!.AbsolutePath);
    }

    /// <summary>
    /// Поля решения администратора необязательны: у ожидающей заявки нет ни даты
    /// рассмотрения, ни причины, и разбор не должен подставлять вместо них значения.
    /// </summary>
    [Fact]
    public async Task GetMineAsync_PendingRequest_ParsesNullableFieldsAsNull()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MineJson(RequestJson(
            status: "Pending",
            about: "null",
            hasSalon: "false",
            reviewedAt: "null",
            rejectionReason: "null")));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        var mine = await api.GetMineAsync();

        Assert.NotNull(mine.Request);
        Assert.Null(mine.Request.About);
        Assert.Null(mine.Request.ReviewedAt);
        Assert.Null(mine.Request.RejectionReason);
        Assert.False(mine.Request.HasSalon);
        Assert.Equal(BarberRequestStatus.Pending, mine.Request.Status);
    }

    /// <summary>
    /// <c>POST</c> шлёт ровно пять полей контракта и ровно по адресу создания: роль в тело
    /// не передаётся, её выдаёт только администратор по заявке.
    /// </summary>
    [Fact]
    public async Task CreateAsync_FullRequest_SendsExactlyContractFields()
    {
        var handler = new StubHandler(HttpStatusCode.Created, RequestJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);
        var request = new CreateBarberRequestRequest(
            "Ева Мастерова",
            7,
            "Екатеринбург",
            "Стригу с 2019 года.",
            true);

        await api.CreateAsync(request);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/barber-requests", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;
        var names = root.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ContractFieldNames, names);
        Assert.Equal("Ева Мастерова", root.GetProperty("displayName").GetString());
        Assert.Equal(7, root.GetProperty("yearsOfExperience").GetInt32());
        Assert.Equal("Екатеринбург", root.GetProperty("city").GetString());
        Assert.Equal("Стригу с 2019 года.", root.GetProperty("about").GetString());
        Assert.True(root.GetProperty("hasSalon").GetBoolean());

        // Разбор в контракт бэкенда: клиент не придумал своё поле и не потерял чужое.
        var parsed = JsonSerializer.Deserialize<CreateBarberRequestRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.Equal(request, parsed);
    }

    /// <summary>
    /// Пустой рассказ о себе уходит как <c>null</c>: пустая строка сохранилась бы на сервере
    /// как рассказ, которого человек не заполнял.
    /// </summary>
    [Fact]
    public async Task CreateAsync_NullAbout_SendsNullProperty()
    {
        var handler = new StubHandler(HttpStatusCode.Created, RequestJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        await api.CreateAsync(new CreateBarberRequestRequest("Ева", 1, "Пермь", null, false));

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("about").ValueKind);
        Assert.False(root.GetProperty("hasSalon").GetBoolean());
    }

    /// <summary>
    /// <c>201</c> возвращает саму заявку: экран показывает статус без второго чтения.
    /// </summary>
    [Fact]
    public async Task CreateAsync_Created_ParsesCreatedRequest()
    {
        var handler = new StubHandler(HttpStatusCode.Created, RequestJson(about: "null"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        var created = await api.CreateAsync(new CreateBarberRequestRequest("Ева", 7, "Пермь", null, false));

        Assert.Equal(Guid.Parse(RequestId), created.Id);
        Assert.Equal(BarberRequestStatus.Pending, created.Status);
        Assert.Equal("Ева Мастерова", created.DisplayName);
        Assert.Equal(7, created.YearsOfExperience);
        Assert.Equal("Екатеринбург", created.City);
        Assert.Null(created.About);
        Assert.True(created.HasSalon);
        Assert.Null(created.ReviewedAt);
        Assert.Null(created.RejectionReason);
    }

    /// <summary>
    /// Активная заявка уже есть: повторная подача — <c>409</c> с публичным кодом, по которому
    /// экран перечитывает статус, а не показывает общую ошибку.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AlreadyPending_ThrowsWithStatusCodeAndCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "An active barber request already exists.",
              "errorCodes": ["barberRequest.create.alreadyPending"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateAsync(new CreateBarberRequestRequest("Ева", 7, "Пермь", null, false)));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("barberRequest.create.alreadyPending", exception.ErrorCodes, StringComparer.Ordinal);
        Assert.Empty(exception.ValidationErrors);
    }

    /// <summary>
    /// Заявку подаёт только клиент: мастер и администратор получают <c>403</c>, и экран
    /// обязан показать это отдельным текстом, а не «попробуйте ещё раз».
    /// </summary>
    [Fact]
    public async Task CreateAsync_NotCustomer_ThrowsForbidden()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "Only a customer can create a barber request.",
              "errorCodes": ["barberRequest.create.forbidden"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Forbidden, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateAsync(new CreateBarberRequestRequest("Ева", 7, "Пермь", null, false)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("barberRequest.create.forbidden", exception.ErrorCodes, StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>400</c> несёт несколько полевых кодов сразу и английские тексты в <c>errors</c>:
    /// экран ветвится по кодам, а тексты берёт из веба, поэтому они должны доехать как есть.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ValidationProblem_ThrowsWithFieldErrors()
    {
        const string problemJson = """
            {
              "title": "Validation failed",
              "status": 400,
              "errorCodes": ["barberProfile.displayName", "barberRequest.city.tooLong"],
              "errors": {
                "displayName": ["The display name is required."],
                "city": ["The city is too long."]
              }
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateAsync(new CreateBarberRequestRequest("  ", 7, "Пермь", null, false)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("barberProfile.displayName", exception.ErrorCodes, StringComparer.Ordinal);
        Assert.Contains("barberRequest.city.tooLong", exception.ErrorCodes, StringComparer.Ordinal);
        Assert.Equal(DisplayNameValidationErrors, exception.ValidationErrors["displayName"]);
        Assert.Equal(CityValidationErrors, exception.ValidationErrors["city"]);
    }

    /// <summary>
    /// Пустое тело успешного ответа — нарушение контракта: показывать «заявки нет» по нему
    /// нельзя, иначе форма открылась бы поверх уже существующей заявки.
    /// </summary>
    /// <remarks>
    /// Проверяется JSON-значение <c>null</c>: полностью нулевая длина падает раньше, в
    /// <c>ReadFromJsonAsync</c> (<c>JsonException</c>), и до проверки клиента не доходит —
    /// так же ведёт себя <c>AccountApi</c>.
    /// </remarks>
    [Fact]
    public async Task GetMineAsync_NullBody_ThrowsInvalidOperationException()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetMineAsync());
    }

    /// <summary>
    /// Успешный <c>POST</c> без заявки в теле — та же ошибка контракта: экран не должен
    /// показывать «Заявка отправлена», не получив самой заявки.
    /// </summary>
    [Fact]
    public async Task CreateAsync_NullBody_ThrowsInvalidOperationException()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberRequestsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.CreateAsync(new CreateBarberRequestRequest("Ева", 7, "Пермь", null, false)));
    }

    /// <summary>
    /// Ответ <c>GET mine</c> с заявкой внутри.
    /// </summary>
    /// <param name="request">JSON заявки.</param>
    private static string MineJson(string request) => $$"""{"request":{{request}}}""";

    /// <summary>
    /// JSON заявки: необязательные поля передаются готовым JSON-значением, чтобы тест мог
    /// прислать и строку, и <c>null</c>.
    /// </summary>
    /// <param name="status">Статус заявки.</param>
    /// <param name="about">JSON рассказа о себе.</param>
    /// <param name="hasSalon">JSON признака салона.</param>
    /// <param name="reviewedAt">JSON момента рассмотрения.</param>
    /// <param name="rejectionReason">JSON причины отказа.</param>
    private static string RequestJson(
        string status = "Pending",
        string about = "\"Стригу с 2019 года.\"",
        string hasSalon = "true",
        string reviewedAt = "null",
        string rejectionReason = "null") => $$"""
        {
          "id": "{{RequestId}}",
          "status": "{{status}}",
          "displayName": "Ева Мастерова",
          "yearsOfExperience": 7,
          "city": "Екатеринбург",
          "about": {{about}},
          "hasSalon": {{hasSalon}},
          "createdAt": "2026-11-01T12:30:00+00:00",
          "reviewedAt": {{reviewedAt}},
          "rejectionReason": {{rejectionReason}}
        }
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    /// <remarks>
    /// Тело ответа необязательно: у <c>204</c> содержимого нет, и клиент не должен его читать.
    /// </remarks>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json")
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
