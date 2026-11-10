using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Фотографии портфолио на реальной PostgreSQL: загрузка multipart, раздача по media-URL,
/// публикация/скрытие, административная блокировка, порядок и лимит.
/// </summary>
/// <remarks>
/// Каждый тест поднимает собственный хост (<see cref="TestApp.StartAsync"/>), поэтому rate
/// limiter не переполняется между сценариями, а корень хранилища — уникальный временный
/// каталог: файлы не появляются в <c>App_Data</c> репозитория.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PortfolioPhotoUploadTests
{
    /// <summary>
    /// Сервер отдаёт перечисления строками, поэтому клиенту нужен конвертер.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Ожидаемые <c>SortOrder</c> после bulk-переупорядочивания: 0..2.
    /// </summary>
    private static readonly int[] ExpectedSortOrders = [0, 1, 2];

    private readonly PostgresFixture _database;

    public PortfolioPhotoUploadTests(PostgresFixture database)
    {
        _database = database;
    }

    /// <summary>
    /// Загрузка кладёт файл в хранилище, публичная карточка получает относительный URL,
    /// а раздача отдаёт тот же байт-в-байт файл с правильными заголовками.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Upload_stores_file_and_serves_it_with_public_cache_headers()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Upload Barber");
        await app.AuthenticateAsync(barber.User.Email);

        using var anonymous = app.CreateClient();
        var content = PngBytes();

        var photoId = await UploadAsync(
            app.Client,
            barber.Profile.Id,
            content,
            description: "Fresh haircut");

        // Собственная карточка: фотография есть и у только что загруженной.
        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var ownPhoto = Assert.Single(ownCard.Photos);
        Assert.Equal(photoId, ownPhoto.Id);

        // Публичная карточка: только видимые, URL относительный.
        var publicCard = await GetCardAsync(
            anonymous,
            $"api/barbers/{barber.Profile.Id}");
        var photo = Assert.Single(publicCard.Photos);
        Assert.Equal(photoId, photo.Id);
        Assert.Equal("Fresh haircut", photo.Description);
        Assert.Equal(0, photo.SortOrder);
        Assert.True(photo.IsPublished);
        Assert.False(photo.IsBlocked);
        Assert.Null(photo.ThumbnailUrl);
        Assert.StartsWith("/api/media/portfolio/", photo.Url, StringComparison.Ordinal);

        // Файл лежит во временном корне теста, а не в App_Data репозитория.
        var fileName = photo.Url[(photo.Url.LastIndexOf('/') + 1)..];
        var filePath = Path.Combine(
            app.StorageRootPath,
            "portfolio",
            $"{barber.Profile.Id:N}",
            fileName);
        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllBytesAsync(filePath));

        using var media = await anonymous.GetAsync(photo.Url);
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal("image/png", media.Content.Headers.ContentType?.MediaType);
        AssertPublicCache(media);
        Assert.Equal("nosniff", Assert.Single(media.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(content, await media.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// Неверный тип, слишком маленькие размеры, превышение 5 МиБ и пустой файл отвечают
    /// <c>400</c> с кодом, а строка в портфолио не появляется.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Upload_rejects_unsupported_type_dimensions_oversize_and_empty_file()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Invalid Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var profileId = barber.Profile.Id;

        var gif = new byte[2048];
        "GIF89a"u8.CopyTo(gif);

        using (var response = await SendUploadAsync(
                   app.Client,
                   profileId,
                   gif,
                   fileName: "photo.gif",
                   contentType: "image/gif"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "barberProfile.uploadPhoto.unsupportedType",
                await response.Content.ReadAsStringAsync());
        }

        using (var response = await SendUploadAsync(
                   app.Client,
                   profileId,
                   PngBytes(width: 200, height: 600)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "barberProfile.uploadPhoto.dimensions",
                await response.Content.ReadAsStringAsync());
        }

        using (var response = await SendUploadAsync(
                   app.Client,
                   profileId,
                   new byte[Constants.MAX_PHOTO_BYTES + 1]))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "barberProfile.uploadPhoto.tooLarge",
                await response.Content.ReadAsStringAsync());
        }

        using (var response = await SendUploadAsync(
                   app.Client,
                   profileId,
                   []))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "barberProfile.uploadPhoto.fileRequired",
                await response.Content.ReadAsStringAsync());
        }

        // Форма без поля файла — тот же «файла нет», а не 500.
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("no file here"), "description");

            using var response = await app.Client.PostAsync(
                $"api/barber-profiles/{profileId}/photos/upload",
                form);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "barberProfile.uploadPhoto.fileRequired",
                await response.Content.ReadAsStringAsync());
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(0, await db.PortfolioPhotos.CountAsync());

        // Ни один отклонённый запрос не оставил файла.
        Assert.False(Directory.Exists(
            Path.Combine(app.StorageRootPath, "portfolio", $"{profileId:N}")));
    }

    /// <summary>
    /// Одиннадцатая фотография — <c>409 barberProfile.addPhoto.limit</c>, и до записи файла.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Upload_returns_conflict_for_the_eleventh_photo()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Limit Barber");

        await SeedPhotosAsync(app, barber.Profile.Id, Constants.MAX_PHOTOS_COUNT);
        await app.AuthenticateAsync(barber.User.Email);

        using var response = await SendUploadAsync(
            app.Client,
            barber.Profile.Id,
            PngBytes());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "barberProfile.addPhoto.limit",
            await response.Content.ReadAsStringAsync());

        Assert.False(Directory.Exists(
            Path.Combine(app.StorageRootPath, "portfolio", $"{barber.Profile.Id:N}")));
    }

    /// <summary>
    /// Чужой барбер, клиент и аноним не могут загрузить фотографию в чужой профиль.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Upload_is_forbidden_for_foreign_barber_customer_and_anonymous()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await data.CreateBookableBarberAsync("Portfolio Owner Barber");
        var foreign = await data.CreateBookableBarberAsync("Portfolio Foreign Barber");

        var customer = await app.CreateUserAsync(
            $"portfolio-customer-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Customer);

        await app.AuthenticateAsync(foreign.User.Email);

        using (var response = await SendUploadAsync(app.Client, owner.Profile.Id, PngBytes()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("auth.forbidden", await response.Content.ReadAsStringAsync());
        }

        await app.AuthenticateAsync(customer.Email);

        using (var response = await SendUploadAsync(app.Client, owner.Profile.Id, PngBytes()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("auth.forbidden", await response.Content.ReadAsStringAsync());
        }

        using var anonymous = app.CreateClient();

        using (var response = await SendUploadAsync(anonymous, owner.Profile.Id, PngBytes()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(0, await db.PortfolioPhotos.CountAsync());
    }

    /// <summary>
    /// Удаление убирает строку и файл, после чего media-URL отвечает <c>404</c> даже владельцу.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Delete_removes_row_and_file_and_media_becomes_not_found()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Delete Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await UploadAsync(app.Client, barber.Profile.Id, PngBytes());

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);
        var filePath = Path.Combine(
            app.StorageRootPath,
            "portfolio",
            $"{barber.Profile.Id:N}",
            photo.Url[(photo.Url.LastIndexOf('/') + 1)..]);
        Assert.True(File.Exists(filePath));

        using (var delete = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.False(File.Exists(filePath));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.Equal(0, await db.PortfolioPhotos.CountAsync());
        }

        // Строки нет — доступ не вычисляется: 404 и владельцу, и анониму.
        using (var media = await app.Client.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.NotFound, media.StatusCode);
        }

        using var anonymous = app.CreateClient();

        using (var media = await anonymous.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.NotFound, media.StatusCode);
        }

        var clearedCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        Assert.Empty(clearedCard.Photos);
    }

    /// <summary>
    /// Скрытие владельцем убирает фото из публичной карточки и из раздачи для анонима,
    /// но оставляет его владельцу и администратору с <c>Cache-Control: private, no-store</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Unpublished_photo_is_hidden_from_public_but_visible_to_owner_and_admin()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Publish Barber");
        var admin = await app.CreateUserAsync(
            $"portfolio-publish-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await UploadAsync(app.Client, barber.Profile.Id, PngBytes());

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var url = Assert.Single(ownCard.Photos).Url;

        using var anonymous = app.CreateClient();

        using (var unpublish = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/publication"))
        {
            Assert.Equal(HttpStatusCode.NoContent, unpublish.StatusCode);
        }

        var publicCard = await GetCardAsync(anonymous, $"api/barbers/{barber.Profile.Id}");
        Assert.Empty(publicCard.Photos);

        using (var anonymousMedia = await anonymous.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        // Владелец видит превью скрытого кадра, и ответ нельзя кэшировать публично.
        using (var ownerMedia = await app.Client.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
            Assert.Equal(
                "nosniff",
                Assert.Single(ownerMedia.Headers.GetValues("X-Content-Type-Options")));
        }

        var afterHide = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var hidden = Assert.Single(afterHide.Photos);
        Assert.False(hidden.IsPublished);
        Assert.False(hidden.IsBlocked);

        await app.AuthenticateAsync(admin.Email);

        using (var adminMedia = await app.Client.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, adminMedia.StatusCode);
            AssertPrivateNoStore(adminMedia);
        }

        // Возврат в карточку: владельческое действие, не блокировка.
        await app.AuthenticateAsync(barber.User.Email);

        using (var publish = await app.Client.PostAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/publication",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        }

        var restored = await GetCardAsync(anonymous, $"api/barbers/{barber.Profile.Id}");
        Assert.Single(restored.Photos);

        using (var anonymousMedia = await anonymous.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, anonymousMedia.StatusCode);
            AssertPublicCache(anonymousMedia);
        }
    }

    /// <summary>
    /// Административная блокировка скрывает кадр от всех, кроме владельца и администратора;
    /// владелец не может ни заблокировать, ни разблокировать; повторные вызовы идемпотентны.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Admin_block_hides_photo_and_only_admin_can_unblock()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Block Barber");
        var foreign = await data.CreateBookableBarberAsync("Portfolio Block Stranger");
        var admin = await app.CreateUserAsync(
            $"portfolio-block-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await UploadAsync(app.Client, barber.Profile.Id, PngBytes());

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var url = Assert.Single(ownCard.Photos).Url;

        var blockUrl =
            $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/block";

        // Владелец блокировать не может — только администратор. Политика AdminOnly
        // отсекает запрос до обработчика, поэтому это 403 middleware без доменного кода;
        // EnsureAdmin внутри обработчика — вторая линия защиты (проверена unit-тестами).
        using (var ownerBlock = await app.Client.PostAsync(blockUrl, content: null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, ownerBlock.StatusCode);
        }

        await app.AuthenticateAsync(admin.Email);

        using (var block = await app.Client.PostAsync(blockUrl, content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        }

        // Повторная блокировка идемпотентна.
        using (var repeatedBlock = await app.Client.PostAsync(blockUrl, content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeatedBlock.StatusCode);
        }

        using var anonymous = app.CreateClient();

        using (var anonymousMedia = await anonymous.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        var publicCard = await GetCardAsync(anonymous, $"api/barbers/{barber.Profile.Id}");
        Assert.Empty(publicCard.Photos);

        await app.AuthenticateAsync(foreign.User.Email);

        using (var foreignMedia = await app.Client.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignMedia.StatusCode);
        }

        await app.AuthenticateAsync(barber.User.Email);

        using (var ownerMedia = await app.Client.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
        }

        // Владелец не может снять блокировку даже прямым запросом.
        using (var ownerUnblock = await app.Client.DeleteAsync(blockUrl))
        {
            Assert.Equal(HttpStatusCode.Forbidden, ownerUnblock.StatusCode);
        }

        await app.AuthenticateAsync(admin.Email);

        using (var adminMedia = await app.Client.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, adminMedia.StatusCode);
            AssertPrivateNoStore(adminMedia);
        }

        using (var unblock = await app.Client.DeleteAsync(blockUrl))
        {
            Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        }

        using (var repeatedUnblock = await app.Client.DeleteAsync(blockUrl))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeatedUnblock.StatusCode);
        }

        var restoredCard = await GetCardAsync(anonymous, $"api/barbers/{barber.Profile.Id}");
        var restored = Assert.Single(restoredCard.Photos);
        Assert.True(restored.IsPublished);
        Assert.False(restored.IsBlocked);

        using (var anonymousMedia = await anonymous.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, anonymousMedia.StatusCode);
        }
    }

    /// <summary>
    /// Bulk-порядок отражается в публичной карточке: <c>SortOrder</c> идёт 0..N−1,
    /// а неполный список отклоняется.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Reorder_endpoint_changes_public_photo_order()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Order Barber");

        await SeedPhotosAsync(app, barber.Profile.Id, 3);
        await app.AuthenticateAsync(barber.User.Email);

        using var anonymous = app.CreateClient();

        var initialCard = await GetCardAsync(anonymous, $"api/barbers/{barber.Profile.Id}");
        Assert.Equal(3, initialCard.Photos.Count);

        var reorderedIds = initialCard.Photos
            .OrderByDescending(photo => photo.SortOrder)
            .Select(photo => photo.Id)
            .ToList();

        using (var reorder = await app.Client.PutAsJsonAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/order",
                   new { photoIds = reorderedIds }))
        {
            Assert.Equal(HttpStatusCode.NoContent, reorder.StatusCode);
        }

        var reorderedCard = await GetCardAsync(anonymous, $"api/barbers/{barber.Profile.Id}");
        Assert.Equal(
            reorderedIds,
            reorderedCard.Photos.Select(photo => photo.Id).ToList());
        Assert.Equal(
            ExpectedSortOrders,
            reorderedCard.Photos.Select(photo => photo.SortOrder).ToArray());

        // Дубли и неполный набор — 400 доменного кода, порядок не меняется.
        using (var invalid = await app.Client.PutAsJsonAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/order",
                   new { photoIds = new[] { reorderedIds[0], reorderedIds[0] } }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains(
                "barberProfile.reorderPhotos.invalid",
                await invalid.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// Админская карточка видит скрытые и заблокированные фотографии, а чек-лист готовности
    /// считает только видимые; публичный ответ никогда не содержит <c>isBlocked = true</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Admin_detail_contains_hidden_photos_while_readiness_counts_visible_only()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Admin Detail Barber");
        var admin = await app.CreateUserAsync(
            $"portfolio-detail-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await SeedPhotosAsync(app, barber.Profile.Id, 3, profile =>
        {
            var photos = profile.PortfolioPhotos.OrderBy(photo => photo.SortOrder).ToList();
            Require(photos[1].Unpublish());
            Require(photos[2].Block());
        });

        await app.AuthenticateAsync(admin.Email);

        using (var detail = await app.Client.GetAsync($"api/admin/barbers/{barber.Profile.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

            var body = await detail.Content.ReadFromJsonAsync<AdminBarberDetailResponse>(JsonOptions);
            Assert.NotNull(body);

            Assert.Equal(3, body.Photos.Count);
            Assert.Contains(body.Photos, photo => !photo.IsPublished);
            Assert.Contains(body.Photos, photo => photo.IsBlocked);

            // Чек-лист отражает публичную карточку: 1 видимая из 3 строк.
            Assert.Equal(1, body.Readiness.PhotosCount);
        }

        using var anonymous = app.CreateClient();
        var publicCard = await GetCardAsync(anonymous, $"api/barbers/{barber.Profile.Id}");

        Assert.Single(publicCard.Photos);
        Assert.DoesNotContain(publicCard.Photos, photo => photo.IsBlocked);
        Assert.DoesNotContain(publicCard.Photos, photo => !photo.IsPublished);
    }

    /// <summary>
    /// Неверные имена файлов в media-URL отвечают <c>404</c>, а не <c>500</c> и не листингом:
    /// регулярка отсекает <c>..</c>, верхний регистр, чужое расширение и отсутствующие строки.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Media_with_traversal_or_bad_file_name_returns_not_found()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Media Guard Barber");

        using var anonymous = app.CreateClient();
        var profileId = barber.Profile.Id;
        var mediaPrefix = $"api/media/portfolio/{profileId}";

        string[] badNames =
        [
            "not-a-hex-name.png",
            "0123456789ABCDEF0123456789ABCDEF.png",
            $"{Guid.CreateVersion7():N}.gif",
            $"{Guid.CreateVersion7():N}.jpg",
            "..%2Fsecret.png",
            "....png",
            "%2e%2e%2fsecret.png"
        ];

        foreach (var fileName in badNames)
        {
            using var response = await anonymous.GetAsync($"{mediaPrefix}/{fileName}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // Литеральный сегмент «..» нормализуется клиентом ещё до отправки и уводит запрос
        // с маршрута media (там отвечает fallback-политика), поэтому обход каталога
        // проверяется кодированным «..%2F» выше: он остаётся внутри имени файла и даёт 404.
    }

    /// <summary>
    /// Посев фотографий без файлов: строки создаются доменным путём, чтобы сценарии порядка
    /// и лимита не зависели от десяти HTTP-загрузок и лимитера.
    /// </summary>
    private static async Task SeedPhotosAsync(
        TestApp app,
        Guid barberProfileId,
        int count,
        Action<BarberProfile>? configure = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles
            .Include(item => item.PortfolioPhotos)
            .SingleAsync(item => item.Id == barberProfileId);

        for (var index = 0; index < count; index++)
        {
            Require(profile.AddUploadedPhoto(
                PortfolioMediaUrl.Build(barberProfileId, Guid.CreateVersion7(), "png"),
                $"Seeded photo {index}"));
        }

        configure?.Invoke(profile);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// POST multipart с одним файлом; вызывающий сам проверяет код ответа.
    /// </summary>
    private static async Task<HttpResponseMessage> SendUploadAsync(
        HttpClient client,
        Guid barberProfileId,
        byte[] content,
        string fileName = "photo.png",
        string contentType = "image/png",
        string? description = null)
    {
        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        if (description is not null)
        {
            form.Add(new StringContent(description), "description");
        }

        return await client.PostAsync(
            $"api/barber-profiles/{barberProfileId}/photos/upload",
            form);
    }

    /// <summary>
    /// Загрузка с проверкой <c>201</c>: возвращает идентификатор созданной фотографии.
    /// </summary>
    private static async Task<Guid> UploadAsync(
        HttpClient client,
        Guid barberProfileId,
        byte[] content,
        string? description = null)
    {
        using var response = await SendUploadAsync(
            client,
            barberProfileId,
            content,
            description: description);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>(JsonOptions);
        Assert.NotNull(body);

        return body.Id;
    }

    /// <summary>
    /// Читает карточку барбера по относительному пути и проверяет <c>200</c>.
    /// </summary>
    private static async Task<BarberProfileResponse> GetCardAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
        Assert.NotNull(card);

        return card;
    }

    /// <summary>
    /// Проверяет запрет кэширования скрытого или заблокированного кадра.
    /// </summary>
    /// <remarks>
    /// На проводе заголовок — <c>private, no-store</c>; <see cref="HttpResponseMessage.Headers"/>
    /// разбирает и заново сериализует директивы в своём порядке, поэтому проверяются флаги,
    /// а не строка целиком.
    /// </remarks>
    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl
            ?? throw new InvalidOperationException("Cache-Control header is missing.");

        Assert.True(cacheControl.Private);
        Assert.True(cacheControl.NoStore);
        Assert.Null(cacheControl.MaxAge);
    }

    /// <summary>
    /// Проверяет публичное суточное кэширование видимого кадра.
    /// </summary>
    private static void AssertPublicCache(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl
            ?? throw new InvalidOperationException("Cache-Control header is missing.");

        Assert.True(cacheControl.Public);
        Assert.Equal(TimeSpan.FromDays(1), cacheControl.MaxAge);
        Assert.False(cacheControl.NoStore);
    }

    /// <summary>
    /// Минимальный PNG с настоящими сигнатурой и <c>IHDR</c>: инспектор читает размеры
    /// из заголовка и не декодирует файл, поэтому остальные байты — заполнитель до 2 КиБ.
    /// </summary>
    private static byte[] PngBytes(int width = 800, int height = 600)
    {
        var content = new byte[2048];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(content);

        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(8), 13);
        "IHDR"u8.CopyTo(content.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(20), (uint)height);
        content[24] = 8;
        content[25] = 6;

        return content;
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }
}
