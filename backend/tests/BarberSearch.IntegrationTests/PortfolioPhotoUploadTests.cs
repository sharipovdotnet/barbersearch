using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
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
    /// Загрузка кладёт файл в хранилище, но публично кадр не виден до одобрения: собственное
    /// чтение отдаёт его как <c>Pending</c>, публичное — пусто, раздача анониму — <c>404</c>.
    /// После решения администратора карточка и раздача работают по обычным правилам.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Upload_stores_file_and_serves_it_publicly_only_after_approval()
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

        // Собственная карточка: фотография есть, но она ждёт проверки.
        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var ownPhoto = Assert.Single(ownCard.Photos);
        Assert.Equal(photoId, ownPhoto.Id);
        Assert.Equal(PhotoModerationStatus.Pending, ownPhoto.ModerationStatus);
        Assert.Null(ownPhoto.RejectionReason);

        // Миниатюра — тот же файл в размере md: своего адреса у неё нет.
        Assert.Equal($"{ownPhoto.Url}?size=md", ownPhoto.ThumbnailUrl);

        // Публичная карточка: только одобренные кадры, поэтому пусто.
        var pendingPublicCard = await GetCardAsync(
            anonymous,
            $"api/barbers/{barber.Profile.Id}");
        Assert.Empty(pendingPublicCard.Photos);

        // Файл уже лежит во временном корне теста, а не в App_Data репозитория.
        var fileName = ownPhoto.Url[(ownPhoto.Url.LastIndexOf('/') + 1)..];
        var filePath = Path.Combine(
            app.StorageRootPath,
            "portfolio",
            $"{barber.Profile.Id:N}",
            fileName);
        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllBytesAsync(filePath));

        // Анониму непубличный кадр не раздаётся, владельцу — да, но без публичного кэша.
        using (var pendingMedia = await anonymous.GetAsync(ownPhoto.Url))
        {
            Assert.Equal(HttpStatusCode.NotFound, pendingMedia.StatusCode);
        }

        using (var ownerMedia = await app.Client.GetAsync(ownPhoto.Url))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
            Assert.Equal(content, await ownerMedia.Content.ReadAsByteArrayAsync());
        }

        await ApproveAsync(app, barber.Profile.Id, photoId);

        // Публичная карточка: одобренный кадр, URL относительный, причина отказа пуста.
        var publicCard = await GetCardAsync(
            anonymous,
            $"api/barbers/{barber.Profile.Id}");
        var photo = Assert.Single(publicCard.Photos);
        Assert.Equal(photoId, photo.Id);
        Assert.Equal("Fresh haircut", photo.Description);
        Assert.Equal(0, photo.SortOrder);
        Assert.True(photo.IsPublished);
        Assert.False(photo.IsBlocked);
        Assert.Equal($"{photo.Url}?size=md", photo.ThumbnailUrl);
        Assert.Equal(PhotoModerationStatus.Approved, photo.ModerationStatus);
        Assert.Null(photo.RejectionReason);
        Assert.StartsWith("/api/media/portfolio/", photo.Url, StringComparison.Ordinal);

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
        var reviewer = await app.CreateUserAsync(
            $"portfolio-limit-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await SeedPhotosAsync(
            app,
            barber.Profile.Id,
            Constants.MAX_PHOTOS_COUNT,
            reviewer.Id);
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

        // Сценарий про скрытие владельцем: кадр должен быть уже одобрен, иначе он скрыт статусом.
        await ApproveAsync(app, barber.Profile.Id, photoId);

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
        Assert.Equal(PhotoModerationStatus.Approved, hidden.ModerationStatus);

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

        // Блокировка — действие над уже опубликованным кадром: сначала одобрение.
        await ApproveAsync(app, barber.Profile.Id, photoId);

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
        Assert.Equal(PhotoModerationStatus.Approved, restored.ModerationStatus);

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
        var reviewer = await app.CreateUserAsync(
            $"portfolio-order-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await SeedPhotosAsync(app, barber.Profile.Id, 3, reviewer.Id);
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

        await SeedPhotosAsync(app, barber.Profile.Id, 3, admin.Id, profile =>
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

            // Админская карточка отдаёт тот же вычисляемый адрес миниатюры, что и остальные.
            Assert.All(
                body.Photos,
                photo => Assert.Equal($"{photo.Url}?size=md", photo.ThumbnailUrl));

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
    /// Видимый кадр отдаёт WebP-вариант с честным кэшем, сильным ETag и <c>304</c> на
    /// повторный условный запрос; исходник и три ширины сосуществуют.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Visible_photo_variant_serves_webp_with_honest_cache_etag_and_not_modified()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Variant Barber");
        await app.AuthenticateAsync(barber.User.Email);

        // Настоящий JPEG: из минимального PNG декодер вариантов ничего не сделает.
        var content = MediaTestImage.Jpeg(1200, 900);
        var photoId = await UploadAsync(
            app.Client,
            barber.Profile.Id,
            content,
            fileName: "photo.jpg",
            contentType: "image/jpeg");

        await ApproveAsync(app, barber.Profile.Id, photoId);

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);
        var mediaId = MediaIdFromUrl(photo.Url);

        using var anonymous = app.CreateClient();

        // Исходник: прежний Content-Type, но новый честный кэш и ETag без суффикса.
        using (var original = await anonymous.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.OK, original.StatusCode);
            Assert.Equal("image/jpeg", original.Content.Headers.ContentType?.MediaType);
            AssertPublicCache(original);
            Assert.Equal($"\"{mediaId:N}\"", RequireEntityTag(original));
            Assert.Equal(
                "nosniff",
                Assert.Single(original.Headers.GetValues("X-Content-Type-Options")));
            Assert.Contains("bytes", original.Headers.AcceptRanges);
            AssertNoVary(original);
            Assert.Equal(content, await original.Content.ReadAsByteArrayAsync());
        }

        var smallUrl = photo.Url + "?size=sm";
        string smallEntityTag;

        using (var small = await anonymous.GetAsync(smallUrl))
        {
            Assert.Equal(HttpStatusCode.OK, small.StatusCode);
            Assert.Equal("image/webp", small.Content.Headers.ContentType?.MediaType);
            AssertPublicCache(small);
            Assert.Equal(
                "nosniff",
                Assert.Single(small.Headers.GetValues("X-Content-Type-Options")));
            Assert.Contains("bytes", small.Headers.AcceptRanges);
            AssertNoVary(small);

            smallEntityTag = RequireEntityTag(small);
            Assert.Equal($"\"{mediaId:N}-sm\"", smallEntityTag);

            var dimensions = MediaTestImage.Dimensions(
                await small.Content.ReadAsByteArrayAsync());
            Assert.Equal(
                MediaTestImage.VariantDimensions(
                    1200,
                    900,
                    PhotoVariantWidths.PORTFOLIO_SMALL),
                dimensions);
        }

        // Тот же ETag: 304 без тела, но с ETag и Cache-Control.
        using (var notModified = await GetWithIfNoneMatchAsync(
                   anonymous,
                   smallUrl,
                   smallEntityTag))
        {
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
            Assert.Equal(smallEntityTag, RequireEntityTag(notModified));
            AssertPublicCache(notModified);
            Assert.Equal(
                "nosniff",
                Assert.Single(notModified.Headers.GetValues("X-Content-Type-Options")));
            AssertNoVary(notModified);
        }

        // «*» совпадает с любым представлением, чужой ETag — обычный 200.
        using (var wildcard = await GetWithIfNoneMatchAsync(anonymous, smallUrl, "*"))
        {
            Assert.Equal(HttpStatusCode.NotModified, wildcard.StatusCode);
            Assert.Empty(await wildcard.Content.ReadAsByteArrayAsync());
        }

        using (var foreign = await GetWithIfNoneMatchAsync(
                   anonymous,
                   smallUrl,
                   $"\"{Guid.CreateVersion7():N}-sm\""))
        {
            Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
            Assert.Equal("image/webp", foreign.Content.Headers.ContentType?.MediaType);
            AssertPublicCache(foreign);
        }

        // Средний и большой варианты — те же байты в другой ширине и со своим суффиксом.
        foreach (var (suffix, width) in new[]
                 {
                     ("md", PhotoVariantWidths.PORTFOLIO_MEDIUM),
                     ("lg", PhotoVariantWidths.PORTFOLIO_LARGE)
                 })
        {
            using var variant = await anonymous.GetAsync($"{photo.Url}?size={suffix}");

            Assert.Equal(HttpStatusCode.OK, variant.StatusCode);
            Assert.Equal("image/webp", variant.Content.Headers.ContentType?.MediaType);
            Assert.Equal($"\"{mediaId:N}-{suffix}\"", RequireEntityTag(variant));
            AssertPublicCache(variant);

            var dimensions = MediaTestImage.Dimensions(
                await variant.Content.ReadAsByteArrayAsync());
            Assert.Equal(
                MediaTestImage.VariantDimensions(1200, 900, width),
                dimensions);
        }
    }

    /// <summary>
    /// Неизвестный, пустой и повторённый <c>size</c> — тот же <c>404</c>, что и отсутствующий
    /// файл, и всегда <c>no-store</c>: набора размеров не существует.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Variant_with_invalid_size_returns_not_found_without_store()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Size Guard Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await UploadAsync(
            app.Client,
            barber.Profile.Id,
            MediaTestImage.Jpeg(1000, 800),
            fileName: "photo.jpg",
            contentType: "image/jpeg");

        await ApproveAsync(app, barber.Profile.Id, photoId);

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);

        using var anonymous = app.CreateClient();

        // Видимость в порядке: валидный размер отвечает 200, значит 404 ниже — про query.
        using (var valid = await anonymous.GetAsync(photo.Url + "?size=sm"))
        {
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        }

        foreach (var query in new[] { "?size=xx", "?size=", "?size=sm&size=md" })
        {
            using var response = await anonymous.GetAsync(photo.Url + query);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains(
                "portfolioMedia.notFound",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            AssertNoStore(response);
            AssertNoVary(response);
        }
    }

    /// <summary>
    /// Неодобренный кадр: вариант скрыт от анонима (<c>404</c> + <c>no-store</c>), а владелец
    /// профиля и администратор видят превью с <c>private, no-store</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Pending_photo_variant_is_private_to_owner_and_admin()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Pending Variant Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var content = MediaTestImage.Jpeg(1000, 800);
        await UploadAsync(
            app.Client,
            barber.Profile.Id,
            content,
            fileName: "photo.jpg",
            contentType: "image/jpeg");

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);
        var mediaId = MediaIdFromUrl(photo.Url);
        var variantUrl = photo.Url + "?size=md";

        using var anonymous = app.CreateClient();

        using (var anonymousVariant = await anonymous.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousVariant.StatusCode);
            Assert.Contains(
                "portfolioMedia.notFound",
                await anonymousVariant.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            AssertNoStore(anonymousVariant);
        }

        using (var ownerVariant = await app.Client.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.OK, ownerVariant.StatusCode);
            Assert.Equal("image/webp", ownerVariant.Content.Headers.ContentType?.MediaType);
            AssertPrivateNoStore(ownerVariant);
            Assert.Equal($"\"{mediaId:N}-md\"", RequireEntityTag(ownerVariant));
            Assert.Equal(
                "nosniff",
                Assert.Single(ownerVariant.Headers.GetValues("X-Content-Type-Options")));
        }

        var admin = await app.CreateUserAsync(
            $"portfolio-variant-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var adminVariant = await app.Client.GetAsync(variantUrl);
        Assert.Equal(HttpStatusCode.OK, adminVariant.StatusCode);
        Assert.Equal("image/webp", adminVariant.Content.Headers.ContentType?.MediaType);
        AssertPrivateNoStore(adminVariant);
    }

    /// <summary>
    /// Скрытый владельцем и заблокированный администратором кадр: тот же <c>?size=</c> отвечает
    /// анониму <c>404</c> + <c>no-store</c>, а владельцу и администратору — 200 + <c>private,
    /// no-store</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Hidden_and_blocked_photo_variants_follow_the_original_visibility_rules()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Hidden Variant Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await UploadAsync(
            app.Client,
            barber.Profile.Id,
            MediaTestImage.Jpeg(1000, 800),
            fileName: "photo.jpg",
            contentType: "image/jpeg");

        await ApproveAsync(app, barber.Profile.Id, photoId);

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);
        var variantUrl = photo.Url + "?size=md";

        using var anonymous = app.CreateClient();

        // Скрытие владельцем: вариант перестаёт быть публичным.
        using (var unpublish = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/publication"))
        {
            Assert.Equal(HttpStatusCode.NoContent, unpublish.StatusCode);
        }

        using (var hiddenAnonymous = await anonymous.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, hiddenAnonymous.StatusCode);
            AssertNoStore(hiddenAnonymous);
        }

        using (var hiddenOwner = await app.Client.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.OK, hiddenOwner.StatusCode);
            Assert.Equal("image/webp", hiddenOwner.Content.Headers.ContentType?.MediaType);
            AssertPrivateNoStore(hiddenOwner);
        }

        var admin = await app.CreateUserAsync(
            $"portfolio-hidden-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var hiddenAdmin = await app.Client.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.OK, hiddenAdmin.StatusCode);
            AssertPrivateNoStore(hiddenAdmin);
        }

        // Возврат в публикацию: снова публичный кэш и всем доступный WebP.
        await app.AuthenticateAsync(barber.User.Email);

        using (var publish = await app.Client.PostAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/publication",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        }

        using (var visible = await anonymous.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            Assert.Equal("image/webp", visible.Content.Headers.ContentType?.MediaType);
            AssertPublicCache(visible);
        }

        // Блокировка администратором: владелец по-прежнему видит превью, аноним — нет.
        await app.AuthenticateAsync(admin.Email);

        using (var block = await app.Client.PostAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/block",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        }

        using (var blockedAnonymous = await anonymous.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, blockedAnonymous.StatusCode);
            AssertNoStore(blockedAnonymous);
        }

        using (var blockedAdmin = await app.Client.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.OK, blockedAdmin.StatusCode);
            AssertPrivateNoStore(blockedAdmin);
        }

        await app.AuthenticateAsync(barber.User.Email);

        using var blockedOwner = await app.Client.GetAsync(variantUrl);
        Assert.Equal(HttpStatusCode.OK, blockedOwner.StatusCode);
        AssertPrivateNoStore(blockedOwner);
    }

    /// <summary>
    /// Удалённый вариант догенерируется на первом чтении, а исходник при этом не меняется:
    /// так работают кадры, загруженные до среза, и кадры с упавшей генерацией на загрузке.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Missing_variant_file_is_generated_lazily_without_touching_the_original()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Lazy Variant Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var content = MediaTestImage.Jpeg(1200, 900);
        var photoId = await UploadAsync(
            app.Client,
            barber.Profile.Id,
            content,
            fileName: "photo.jpg",
            contentType: "image/jpeg");

        await ApproveAsync(app, barber.Profile.Id, photoId);

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);
        var mediaId = MediaIdFromUrl(photo.Url);
        var fileName = photo.Url[(photo.Url.LastIndexOf('/') + 1)..];
        var originalPath = Path.Combine(
            app.StorageRootPath,
            "portfolio",
            $"{barber.Profile.Id:N}",
            fileName);

        Assert.Equal(3, VariantFiles(app, barber.Profile.Id, mediaId).Length);

        // Имитируем кадр, загруженный до среза: вариантов на диске нет.
        foreach (var variantPath in VariantFiles(app, barber.Profile.Id, mediaId))
        {
            File.Delete(variantPath);
        }

        Assert.Empty(VariantFiles(app, barber.Profile.Id, mediaId));

        using var anonymous = app.CreateClient();

        using (var lazy = await anonymous.GetAsync(photo.Url + "?size=md"))
        {
            Assert.Equal(HttpStatusCode.OK, lazy.StatusCode);
            Assert.Equal("image/webp", lazy.Content.Headers.ContentType?.MediaType);
            Assert.Equal($"\"{mediaId:N}-md\"", RequireEntityTag(lazy));
            AssertPublicCache(lazy);

            var dimensions = MediaTestImage.Dimensions(
                await lazy.Content.ReadAsByteArrayAsync());
            Assert.Equal(
                MediaTestImage.VariantDimensions(
                    1200,
                    900,
                    PhotoVariantWidths.PORTFOLIO_MEDIUM),
                dimensions);
        }

        // Файл варианта записан, исходник остался байт в байт прежним.
        Assert.True(File.Exists(VariantPath(app, barber.Profile.Id, mediaId, "md")));
        Assert.Equal(content, await File.ReadAllBytesAsync(originalPath));
    }

    /// <summary>
    /// Исходник уже целевой ширины: вариант не создаётся, <c>?size=</c> отдаёт исходник
    /// с его <c>Content-Type</c> и ETag, а не <c>404</c> и не растянутую картинку.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Variant_wider_than_the_source_serves_the_original_file()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Narrow Source Barber");
        await app.AuthenticateAsync(barber.User.Email);

        // 400 px: md (512) и lg (1024) невозможны, sm (256) возможен.
        var content = MediaTestImage.Jpeg(400, 300);
        var photoId = await UploadAsync(
            app.Client,
            barber.Profile.Id,
            content,
            fileName: "photo.jpg",
            contentType: "image/jpeg");

        await ApproveAsync(app, barber.Profile.Id, photoId);

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);
        var mediaId = MediaIdFromUrl(photo.Url);

        using var anonymous = app.CreateClient();

        foreach (var suffix in new[] { "md", "lg" })
        {
            using var response = await anonymous.GetAsync($"{photo.Url}?size={suffix}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal($"\"{mediaId:N}\"", RequireEntityTag(response));
            AssertPublicCache(response);
            Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        }

        Assert.False(File.Exists(VariantPath(app, barber.Profile.Id, mediaId, "md")));
        Assert.False(File.Exists(VariantPath(app, barber.Profile.Id, mediaId, "lg")));

        using var small = await anonymous.GetAsync(photo.Url + "?size=sm");
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
        Assert.Equal("image/webp", small.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"\"{mediaId:N}-sm\"", RequireEntityTag(small));
    }

    /// <summary>
    /// Удаление кадра убирает исходник и все WebP-варианты, поэтому старый <c>?size=</c>
    /// отвечает <c>404</c> с <c>no-store</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Delete_removes_variants_and_the_old_size_url_is_not_found()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Portfolio Delete Variants Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await UploadAsync(
            app.Client,
            barber.Profile.Id,
            MediaTestImage.Jpeg(1200, 900),
            fileName: "photo.jpg",
            contentType: "image/jpeg");

        await ApproveAsync(app, barber.Profile.Id, photoId);

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos);
        var mediaId = MediaIdFromUrl(photo.Url);
        var fileName = photo.Url[(photo.Url.LastIndexOf('/') + 1)..];
        var originalPath = Path.Combine(
            app.StorageRootPath,
            "portfolio",
            $"{barber.Profile.Id:N}",
            fileName);

        Assert.Equal(3, VariantFiles(app, barber.Profile.Id, mediaId).Length);

        using (var delete = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.False(File.Exists(originalPath));
        Assert.Empty(VariantFiles(app, barber.Profile.Id, mediaId));

        using var anonymous = app.CreateClient();

        using var stale = await anonymous.GetAsync(photo.Url + "?size=md");
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Contains(
            "portfolioMedia.notFound",
            await stale.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        AssertNoStore(stale);
    }

    /// <summary>
    /// Посев фотографий без файлов: строки создаются доменным путём, чтобы сценарии порядка
    /// и лимита не зависели от десяти HTTP-загрузок и лимитера.
    /// </summary>
    /// <remarks>
    /// Премодерация: каждая новая строка создаётся <c>Pending</c>. Сценариям, которые проверяют
    /// публичную видимость, нужен уже одобренный набор, поэтому здесь же вызывается
    /// <c>Approve</c> от имени администратора. Файлов нет — раздача не участвует.
    /// </remarks>
    private static async Task SeedPhotosAsync(
        TestApp app,
        Guid barberProfileId,
        int count,
        Guid reviewerId,
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

        foreach (var photo in profile.PortfolioPhotos
                     .Where(photo => photo.ModerationStatus == PhotoModerationStatus.Pending))
        {
            Require(photo.Approve(reviewerId, DateTimeOffset.UtcNow));
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
        string? description = null,
        string fileName = "photo.png",
        string contentType = "image/png")
    {
        using var response = await SendUploadAsync(
            client,
            barberProfileId,
            content,
            fileName: fileName,
            contentType: contentType,
            description: description);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>(JsonOptions);
        Assert.NotNull(body);

        return body.Id;
    }

    /// <summary>
    /// Одобряет загруженный кадр администратором: cookie-сессия с <c>X-CSRF-TOKEN</c>, как
    /// браузер. Отдельный администратор на вызов — тесту не приходится менять личность
    /// основного клиента, который остаётся владельцем профиля.
    /// </summary>
    private static async Task ApproveAsync(TestApp app, Guid barberProfileId, Guid photoId)
    {
        var admin = await app.CreateUserAsync(
            $"portfolio-approval-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await using var session = await app.SignInWithCookieAsync(admin.Email);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/approval");
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);

        using var response = await session.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
    /// Проверяет честный кэш видимого кадра: <c>public, max-age=0, must-revalidate</c>.
    /// </summary>
    /// <remarks>
    /// Суточного <c>max-age</c> больше нет: браузер обязан перепроверять кадр, иначе скрытый
    /// или заменённый файл показывался бы из кэша. <see cref="HttpResponseMessage.Headers"/>
    /// разбирает директивы в своём порядке, поэтому проверяются флаги, а не строка целиком.
    /// </remarks>
    private static void AssertPublicCache(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl
            ?? throw new InvalidOperationException("Cache-Control header is missing.");

        Assert.True(cacheControl.Public);
        Assert.Equal(TimeSpan.Zero, cacheControl.MaxAge);
        Assert.True(cacheControl.MustRevalidate);
        Assert.False(cacheControl.NoStore);
    }

    /// <summary>
    /// Проверяет запрет кэширования отказа: <c>Cache-Control: no-store</c>.
    /// </summary>
    private static void AssertNoStore(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl
            ?? throw new InvalidOperationException("Cache-Control header is missing.");

        Assert.True(cacheControl.NoStore);
        Assert.False(cacheControl.Public);
    }

    /// <summary>
    /// Проверяет сильный <c>ETag</c> и возвращает его значение вместе с кавычками.
    /// </summary>
    private static string RequireEntityTag(HttpResponseMessage response)
    {
        var entityTag = response.Headers.ETag
            ?? throw new InvalidOperationException("ETag header is missing.");

        Assert.False(entityTag.IsWeak);

        return entityTag.Tag.ToString();
    }

    /// <summary>
    /// Проверяет, что ответ не объявляет <c>Vary</c>: представление выбирает URL, не <c>Accept</c>.
    /// </summary>
    private static void AssertNoVary(HttpResponseMessage response)
        => Assert.False(
            response.Headers.Contains("Vary"),
            "The media response must not carry a Vary header.");

    /// <summary>
    /// GET с заголовком <c>If-None-Match</c>: условный запрос к тому же URL.
    /// </summary>
    private static async Task<HttpResponseMessage> GetWithIfNoneMatchAsync(
        HttpClient client,
        string url,
        string entityTag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("If-None-Match", entityTag);

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Идентификатор медиа из media-URL: из него собираются имена вариантов и <c>ETag</c>.
    /// </summary>
    private static Guid MediaIdFromUrl(string url)
    {
        if (!PortfolioMediaUrl.TryParse(url, out _, out var mediaId, out _))
        {
            throw new InvalidOperationException($"Unexpected portfolio media URL: {url}");
        }

        return mediaId;
    }

    /// <summary>
    /// Файлы вариантов кадра на диске теста.
    /// </summary>
    private static string[] VariantFiles(TestApp app, Guid barberProfileId, Guid mediaId)
    {
        var directoryPath = Path.Combine(
            app.StorageRootPath,
            "portfolio",
            $"{barberProfileId:N}");

        return Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, PhotoVariantWidths.FilePattern(mediaId))
            : [];
    }

    /// <summary>
    /// Путь производного файла: <c>{mediaId:N}-{suffix}.webp</c> рядом с исходником.
    /// </summary>
    private static string VariantPath(
        TestApp app,
        Guid barberProfileId,
        Guid mediaId,
        string suffix)
        => Path.Combine(
            app.StorageRootPath,
            "portfolio",
            $"{barberProfileId:N}",
            $"{mediaId:N}-{suffix}.webp");

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
