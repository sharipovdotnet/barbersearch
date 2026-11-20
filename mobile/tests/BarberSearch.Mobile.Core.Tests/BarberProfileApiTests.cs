using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.ReorderBarberProfilePhotos;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Contracts.PortfolioPhotos.UpdatePortfolioPhotoDescription;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента собственного профиля мастера: маршруты, тела и разбор ответов.
/// </summary>
/// <remarks>
/// Все дочерние ресурсы адресуются через профиль, поэтому проверяется и путь
/// (<c>api/barber-profiles/{id}/...</c>), и метод. Записи отвечают <c>204</c> — их тело
/// не читается, и тест это фиксирует пустым ответом.
/// </remarks>
public sealed class BarberProfileApiTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("9e100000-0000-4000-8000-0000000000d1");
    private static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid HairServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000003");
    private static readonly Guid SpecializationId = Guid.Parse("9e100000-0000-4000-8000-000000000009");
    private static readonly Guid CreatedId = Guid.Parse("9e100000-0000-4000-8000-0000000000a2");

    [Fact]
    public async Task GetOwnAsync_Profile_DeserialisesOwnCard()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ProfileJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        var profile = await api.GetOwnAsync(BarberProfileId);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/barber-profiles/", BarberProfileId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(BarberProfileId, profile.Id);
        Assert.Equal(UserId, profile.UserId);
        Assert.Equal("Тимур Мастеров", profile.DisplayName);
        Assert.Equal(Grade.SeniorBarber, profile.Grade);
        Assert.Equal(11, profile.YearsOfExperience);
        Assert.Equal(4.8m, profile.Rating);
        Assert.False(profile.IsVerified);
        Assert.Single(profile.Services);
        Assert.Single(profile.Salons);
    }

    [Fact]
    public async Task GetOwnAsync_NullBody_ThrowsInsteadOfReturningNothing()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetOwnAsync(BarberProfileId));
    }

    [Fact]
    public async Task CreateAsync_Request_SendsBodyAndParsesCreatedId()
    {
        var handler = new StubHandler(
            HttpStatusCode.Created,
            $$"""{"id":"{{CreatedId:D}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        var id = await api.CreateAsync(new BarberProfileCreateRequest(
            "Тимур",
            11,
            null,
            [Language.Ru],
            null,
            null,
            [PaymentMethod.Cash],
            null,
            null,
            null,
            Grade.SeniorBarber));

        Assert.Equal(CreatedId, id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/barber-profiles", handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<BarberProfileCreateRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Тимур", parsed.DisplayName);
        Assert.Equal(11, parsed.YearsOfExperience);
        Assert.Equal([Language.Ru], parsed.Languages);
        Assert.Equal([PaymentMethod.Cash], parsed.PaymentMethods);
        Assert.Equal(Grade.SeniorBarber, parsed.Grade);
    }

    [Fact]
    public async Task CreateAsync_EmptyCreatedId_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateAsync(CreateRequest()));
    }

    [Fact]
    public async Task UpdateAsync_Request_PutsWholeProfile()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        var request = new BarberProfileUpdateRequest(
            "Тимур Обновлённый",
            12,
            "Новое био",
            [Language.Ru],
            [],
            [],
            [PaymentMethod.Cash],
            [],
            [],
            "До 10 минут.",
            []);

        await api.UpdateAsync(BarberProfileId, request);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/barber-profiles/", BarberProfileId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<BarberProfileUpdateRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Тимур Обновлённый", parsed.DisplayName);
        Assert.Equal(12, parsed.YearsOfExperience);
    }

    [Fact]
    public async Task GetSalonsAsync_Salons_DeserialisesSummaryWithoutPaging()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SalonsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        var salons = await api.GetSalonsAsync(BarberProfileId);

        Assert.Equal(
            string.Concat("/api/barber-profiles/", BarberProfileId.ToString("D"), "/salons"),
            handler.LastRequestUri!.AbsolutePath);

        var salon = Assert.Single(salons);

        Assert.Equal(SalonId, salon.SalonId);
        Assert.True(salon.IsPrimary);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.Equal(PlaceKind.Salon, salon.Kind);
        Assert.True(salon.IsListed);
        Assert.Equal("Ленина, 1", salon.Address);
    }

    [Fact]
    public async Task GetSalonsAsync_EmptyArray_ReturnsEmptyList()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        var salons = await api.GetSalonsAsync(BarberProfileId);

        // Пустой список — законное «салонов ещё нет», а не ошибка контракта.
        Assert.Empty(salons);
    }

    [Fact]
    public async Task AddSalonAsync_SalonAndPrimary_SendsContractBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.AddSalonAsync(BarberProfileId, SalonId, isPrimary: true);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/barber-profiles/", BarberProfileId.ToString("D"), "/salons"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<AddBarberProfileSalonRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(SalonId, parsed.SalonId);
        Assert.True(parsed.IsPrimary);
    }

    [Fact]
    public async Task RemoveSalonAsync_ValidIds_DeletesSalonPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.RemoveSalonAsync(BarberProfileId, SalonId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/salons/",
                SalonId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SetPrimarySalonAsync_ValidIds_PutsPrimaryPathWithoutBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.SetPrimarySalonAsync(BarberProfileId, SalonId);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/salons/",
                SalonId.ToString("D"),
                "/primary"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task AddServiceAsync_Request_SendsContractBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.AddServiceAsync(
            BarberProfileId,
            new AddBarberProfileServiceRequest(HairServiceId, 2400m, 45));

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/barber-profiles/", BarberProfileId.ToString("D"), "/services"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<AddBarberProfileServiceRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(HairServiceId, parsed.HairServiceId);
        Assert.Equal(2400m, parsed.Price);
        Assert.Equal(45, parsed.DurationMinutes);
    }

    [Fact]
    public async Task UpdateServiceAsync_Request_PutsServicePathAndBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.UpdateServiceAsync(
            BarberProfileId,
            HairServiceId,
            new UpdateBarberProfileServiceRequest(2600m, 50));

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/services/",
                HairServiceId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<UpdateBarberProfileServiceRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(2600m, parsed.Price);
        Assert.Equal(50, parsed.DurationMinutes);
    }

    [Fact]
    public async Task RemoveServiceAsync_ValidIds_DeletesServicePath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.RemoveServiceAsync(BarberProfileId, HairServiceId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/services/",
                HairServiceId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AddSpecializationAsync_ValidIds_PostsSpecializationPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.AddSpecializationAsync(BarberProfileId, SpecializationId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/specializations/",
                SpecializationId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task RemoveSpecializationAsync_ValidIds_DeletesSpecializationPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.RemoveSpecializationAsync(BarberProfileId, SpecializationId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/specializations/",
                SpecializationId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateAsync_Forbidden_ThrowsWithStatusCode()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "The barber profile belongs to another user.",
              "errorCodes": ["auth.forbidden"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Forbidden, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.UpdateAsync(
                BarberProfileId,
                new BarberProfileUpdateRequest(
                    "Тимур",
                    11,
                    null,
                    [Language.Ru],
                    [],
                    [],
                    [PaymentMethod.Cash],
                    [],
                    [],
                    "До 10 минут.",
                    [])));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("auth.forbidden", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AddSalonAsync_EmptySalonId_ThrowsWithoutRequest()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => api.AddSalonAsync(BarberProfileId, Guid.Empty, isPrimary: false));

        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task UploadPhotoAsync_Multipart_SendsFileAndDescriptionAndReturnsId()
    {
        var handler = new StubHandler(
            HttpStatusCode.Created,
            $$"""{"id":"{{CreatedId:D}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0];

        var id = await api.UploadPhotoAsync(
            BarberProfileId,
            new PortfolioPhotoUpload(bytes, "portfolio.jpg", "image/jpeg", "Работа"));

        Assert.Equal(CreatedId, id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/photos/upload"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.StartsWith("multipart/form-data", handler.LastContentType, StringComparison.Ordinal);
        Assert.Equal("file", handler.FileFieldName);
        Assert.Equal("portfolio.jpg", handler.FileName);
        Assert.Equal("image/jpeg", handler.FileContentType);
        Assert.Equal(bytes, handler.FileBytes);
        Assert.Equal("Работа", handler.MultipartFields["description"]);
    }

    [Fact]
    public async Task UploadPhotoAsync_NoDescription_SendsEmptyDescriptionPart()
    {
        var handler = new StubHandler(
            HttpStatusCode.Created,
            $$"""{"id":"{{CreatedId:D}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.UploadPhotoAsync(
            BarberProfileId,
            new PortfolioPhotoUpload([1, 2, 3], "portfolio.jpg", "image/jpeg", null));

        // Поле description присутствует всегда: сервер сам превращает пустую строку в null.
        Assert.Equal(string.Empty, handler.MultipartFields["description"]);
    }

    [Fact]
    public async Task UploadPhotoAsync_CreatedWithoutId_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.UploadPhotoAsync(
                BarberProfileId,
                new PortfolioPhotoUpload([1], "portfolio.jpg", "image/jpeg", null)));
    }

    [Fact]
    public async Task RemovePhotoAsync_ValidIds_DeletesPhotoPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.RemovePhotoAsync(BarberProfileId, SpecializationId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/photos/",
                SpecializationId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdatePhotoDescriptionAsync_Trimmed_PutsNormalizedBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.UpdatePhotoDescriptionAsync(
            BarberProfileId,
            SpecializationId,
            "  Подпись  ");

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/photos/",
                SpecializationId.ToString("D"),
                "/description"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<UpdatePortfolioPhotoDescriptionRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Подпись", parsed.Description);
    }

    [Fact]
    public async Task UpdatePhotoDescriptionAsync_Whitespace_SendsNull()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.UpdatePhotoDescriptionAsync(BarberProfileId, SpecializationId, "   ");

        var parsed = JsonSerializer.Deserialize<UpdatePortfolioPhotoDescriptionRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Null(parsed.Description);
    }

    [Fact]
    public async Task ReorderPhotosAsync_FullList_PutsOrderBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);
        Guid[] photoIds = [SpecializationId, CreatedId];

        await api.ReorderPhotosAsync(BarberProfileId, photoIds);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/photos/order"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<ReorderBarberProfilePhotosRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(photoIds, parsed.PhotoIds);
    }

    [Fact]
    public async Task PublishPhotoAsync_ValidIds_PostsPublicationPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.PublishPhotoAsync(BarberProfileId, SpecializationId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/photos/",
                SpecializationId.ToString("D"),
                "/publication"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task UnpublishPhotoAsync_ValidIds_DeletesPublicationPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await api.UnpublishPhotoAsync(BarberProfileId, SpecializationId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/photos/",
                SpecializationId.ToString("D"),
                "/publication"),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UploadPhotoAsync_EmptyProfileId_ThrowsWithoutRequest()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberProfileApi(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => api.UploadPhotoAsync(
                Guid.Empty,
                new PortfolioPhotoUpload([1], "portfolio.jpg", "image/jpeg", null)));

        Assert.Null(handler.LastRequestUri);
    }

    private static BarberProfileCreateRequest CreateRequest() => new(
        "Тимур",
        11,
        null,
        [Language.Ru],
        null,
        null,
        [PaymentMethod.Cash],
        null,
        null,
        null,
        Grade.SeniorBarber);

    private static string ProfileJson() => $$"""
        {
          "id": "{{BarberProfileId:D}}",
          "userId": "{{UserId:D}}",
          "avatarUrl": null,
          "displayName": "Тимур Мастеров",
          "grade": "SeniorBarber",
          "bio": null,
          "yearsOfExperience": 11,
          "languages": ["Ru"],
          "certificatesAndTraining": [],
          "rating": 4.8,
          "reviewsCount": 12,
          "achievements": [],
          "paymentMethods": ["Cash"],
          "communicationFormat": [],
          "goodies": [],
          "latePolicy": "До 10 минут.",
          "isActive": true,
          "isVerified": false,
          "services": [
            {
              "barberServiceId": "9e100000-0000-4000-8000-000000000004",
              "hairServiceId": "{{HairServiceId:D}}",
              "name": "Стрижка",
              "category": "Haircut",
              "defaultDurationMinutes": 40,
              "price": 2400,
              "durationMinutes": 45,
              "isActive": false
            }
          ],
          "salons": [
            {
              "id": "9e100000-0000-4000-8000-000000000010",
              "salonId": "{{SalonId:D}}",
              "name": "E2E Studio",
              "address": "Ленина, 1",
              "city": "Москва",
              "country": "Россия",
              "timeZoneId": "Europe/Moscow",
              "kind": "Salon",
              "isPrimary": true,
              "workingSchedules": []
            }
          ],
          "specializations": [],
          "additionalServices": [],
          "photos": []
        }
        """;

    private static string SalonsJson() => $$"""
        [
          {
            "salonId": "{{SalonId:D}}",
            "name": "E2E Studio",
            "city": "Москва",
            "address": "Ленина, 1",
            "timeZoneId": "Europe/Moscow",
            "isPrimary": true,
            "country": "Россия",
            "postalCode": null,
            "latitude": 55.7558,
            "longitude": 37.6173,
            "phoneNumber": null,
            "websiteUrl": null,
            "ownerUserId": "{{UserId:D}}",
            "moderationStatus": "Active",
            "rejectionReason": null,
            "kind": "Salon",
            "isListed": true
          }
        ]
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json")
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        public string? LastContentType { get; private set; }

        public string? FileFieldName { get; private set; }

        public string? FileName { get; private set; }

        public string? FileContentType { get; private set; }

        public byte[]? FileBytes { get; private set; }

        public Dictionary<string, string> MultipartFields { get; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            // Тело читается здесь, пока запрос ещё жив: после возврата клиент disposed
            // HttpRequestMessage и составные части формы уже недоступны.
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    var disposition = part.Headers.ContentDisposition;

                    if (string.Equals(
                            disposition?.Name?.Trim('"'),
                            "file",
                            StringComparison.Ordinal))
                    {
                        FileFieldName = disposition?.Name?.Trim('"');
                        FileName = disposition?.FileName?.Trim('"');
                        FileContentType = part.Headers.ContentType?.MediaType;
                        FileBytes = await part.ReadAsByteArrayAsync(cancellationToken);
                    }
                    else if (disposition?.Name is { } name)
                    {
                        MultipartFields[name.Trim('"')] =
                            await part.ReadAsStringAsync(cancellationToken);
                    }
                }
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            };
        }
    }
}
