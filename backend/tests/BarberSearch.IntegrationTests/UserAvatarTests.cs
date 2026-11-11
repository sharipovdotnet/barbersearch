using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Аватар пользователя на реальной PostgreSQL: multipart-загрузка, CSRF cookie-мутации,
/// раздача по media-URL с заголовками, замена и удаление, аватар на публичной карточке
/// барбера и регрессия «сохранение имени после загрузки аватара».
/// </summary>
/// <remarks>
/// Каждый тест поднимает собственный хост (<see cref="TestApp.StartAsync"/>): rate limiter
/// живёт в памяти инстанса, а корень хранилища — уникальный временный каталог, поэтому файлы
/// не появляются в <c>App_Data</c> репозитория.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class UserAvatarTests
{
    /// <summary>
    /// Карточка барбера содержит доменные enum'ы; сервер отдаёт их строками.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public UserAvatarTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Upload_stores_avatar_serves_it_to_the_owner_and_publishes_it_only_after_approval()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-upload"));
        await app.AuthenticateAsync(user.Email);

        var content = PngBytes();
        var avatarUrl = await UploadAvatarAsync(app.Client, content);

        Assert.StartsWith(
            Constants.AVATAR_MEDIA_PATH_PREFIX + "/",
            avatarUrl,
            StringComparison.Ordinal);
        Assert.Contains($"/{user.Id:D}/", avatarUrl, StringComparison.Ordinal);

        // Файл лёг в {RootPath}/avatars/{userId:N}/{mediaId:N}.png.
        var fileName = avatarUrl[(avatarUrl.LastIndexOf('/') + 1)..];
        var filePath = Path.Combine(app.StorageRootPath, "avatars", $"{user.Id:N}", fileName);

        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllBytesAsync(filePath));

        // GET users/me отдаёт тот же относительный URL и состояние «ждёт проверки».
        var profile = await GetMyProfileAsync(app.Client);
        Assert.Equal(avatarUrl, profile.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Pending, profile.AvatarModerationStatus);
        Assert.Null(profile.AvatarRejectionReason);

        // Пока кадр не одобрен, публично его нет: аноним получает тот же 404, что и без файла.
        using var anonymous = app.CreateClient();

        using (var anonymousMedia = await anonymous.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        // Владелец видит превью нового кадра в кабинете, но ответ нельзя кэшировать публично.
        using (var ownerMedia = await app.Client.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            Assert.Equal("image/png", ownerMedia.Content.Headers.ContentType?.MediaType);
            AssertPrivateNoStore(ownerMedia);
            Assert.Equal(
                "nosniff",
                Assert.Single(ownerMedia.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal(content, await ownerMedia.Content.ReadAsByteArrayAsync());
        }

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(admin, user.Id);

        var approved = await GetMyProfileAsync(app.Client);
        Assert.Equal(avatarUrl, approved.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Approved, approved.AvatarModerationStatus);
        Assert.Null(approved.AvatarRejectionReason);

        // Одобренный кадр раздаётся анонимно: MIME-тип, публичный суточный кэш, nosniff, диапазоны.
        using var media = await anonymous.GetAsync(avatarUrl);

        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal("image/png", media.Content.Headers.ContentType?.MediaType);
        AssertPublicCache(media);
        Assert.Equal(
            "nosniff",
            Assert.Single(media.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("bytes", media.Headers.AcceptRanges);
        Assert.Equal(content, await media.Content.ReadAsByteArrayAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Upload_is_unauthorized_for_anonymous()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var response = await SendAvatarAsync(app.Client, PngBytes());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(app.StorageRootPath, "avatars")));
    }

    [Fact(Timeout = 180_000)]
    public async Task Upload_rejects_empty_oversized_and_unsupported_files()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-invalid"));
        await app.AuthenticateAsync(user.Email);

        using (var empty = await SendAvatarAsync(app.Client, []))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Contains(
                "user.uploadAvatar.fileRequired",
                await empty.Content.ReadAsStringAsync());
        }

        // Форма без поля файла — тот же «файла нет», а не 500. Пустая multipart-форма
        // без единой части отклоняется биндером ещё до действия, поэтому здесь есть
        // постороннее текстовое поле: оно и делает форму валидной.
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("no file here"), "comment");

            using var missingField = await app.Client.PostAsync("api/users/me/avatar", form);

            Assert.Equal(HttpStatusCode.BadRequest, missingField.StatusCode);
            Assert.Contains(
                "user.uploadAvatar.fileRequired",
                await missingField.Content.ReadAsStringAsync());
        }

        using (var tooLarge = await SendAvatarAsync(
                   app.Client,
                   new byte[Constants.MAX_PHOTO_BYTES + 1]))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
            Assert.Contains(
                "user.uploadAvatar.tooLarge",
                await tooLarge.Content.ReadAsStringAsync());
        }

        var gif = new byte[2048];
        "GIF89a"u8.CopyTo(gif);

        using (var unsupported = await SendAvatarAsync(
                   app.Client,
                   gif,
                   fileName: "avatar.gif",
                   contentType: "image/gif"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
            Assert.Contains(
                "barberProfile.uploadPhoto.unsupportedType",
                await unsupported.Content.ReadAsStringAsync());
        }

        var profile = await GetMyProfileAsync(app.Client);
        Assert.Null(profile.AvatarUrl);
        Assert.False(Directory.Exists(
            Path.Combine(app.StorageRootPath, "avatars", $"{user.Id:N}")));
    }

    [Fact(Timeout = 180_000)]
    public async Task Upload_with_cookie_session_requires_csrf_and_works_with_the_token()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-csrf"));

        await using var session = await app.SignInWithCookieAsync(user.Email);

        // Multipart-мутация без пары cookie + заголовок отклоняется общим фильтром.
        using (var form = new MultipartFormDataContent())
        {
            form.Add(
                new ByteArrayContent(PngBytes()),
                "file",
                "avatar.png");

            using var response = await session.Client.PostAsync("api/users/me/avatar", form);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "auth.csrf.invalid",
                await response.Content.ReadAsStringAsync());
        }

        var profile = await GetMyProfileAsync(session.Client);
        Assert.Null(profile.AvatarUrl);

        // С токеном та же cookie-сессия загружает файл.
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/users/me/avatar");
        using var validForm = new MultipartFormDataContent();
        validForm.Add(new ByteArrayContent(PngBytes()), "file", "avatar.png");
        request.Content = validForm;
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);

        using var created = await session.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<AvatarUploadResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.StartsWith(Constants.AVATAR_MEDIA_PATH_PREFIX + "/", body.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Pending, body.ModerationStatus);
    }

    [Fact(Timeout = 180_000)]
    public async Task Replacing_avatar_makes_the_old_url_not_found_and_the_new_one_waits_for_approval()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-replace"));
        await app.AuthenticateAsync(user.Email);

        var firstUrl = await UploadAvatarAsync(app.Client, PngBytes());
        var secondUrl = await UploadAvatarAsync(app.Client, PngBytes(width: 600, height: 800));

        Assert.NotEqual(firstUrl, secondUrl);

        using var anonymous = app.CreateClient();

        // Точное сравнение сохранённого URL: старый файл после замены — 404 даже администратору.
        using (var oldMedia = await anonymous.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, oldMedia.StatusCode);
        }

        // Новый кадр ещё не одобрен — анониму 404, владельцу 200 с private/no-store.
        using (var newAnonymousMedia = await anonymous.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, newAnonymousMedia.StatusCode);
        }

        using (var newOwnerMedia = await app.Client.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.OK, newOwnerMedia.StatusCode);
            AssertPrivateNoStore(newOwnerMedia);
        }

        var pending = await GetMyProfileAsync(app.Client);
        Assert.Equal(secondUrl, pending.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Pending, pending.AvatarModerationStatus);

        // Старый файл удалён, новый лежит на диске.
        var firstFile = firstUrl[(firstUrl.LastIndexOf('/') + 1)..];

        Assert.False(File.Exists(
            Path.Combine(app.StorageRootPath, "avatars", $"{user.Id:N}", firstFile)));

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(admin, user.Id);

        using (var newMedia = await anonymous.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.OK, newMedia.StatusCode);
            AssertPublicCache(newMedia);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Delete_removes_avatar_is_idempotent_and_clears_profile()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-delete"));
        await app.AuthenticateAsync(user.Email);

        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());
        var fileName = avatarUrl[(avatarUrl.LastIndexOf('/') + 1)..];
        var filePath = Path.Combine(app.StorageRootPath, "avatars", $"{user.Id:N}", fileName);

        using (var delete = await app.Client.DeleteAsync("api/users/me/avatar"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.False(File.Exists(filePath));

        using var anonymous = app.CreateClient();

        using (var media = await anonymous.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, media.StatusCode);
        }

        var profile = await GetMyProfileAsync(app.Client);
        Assert.Null(profile.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Approved, profile.AvatarModerationStatus);
        Assert.Null(profile.AvatarRejectionReason);

        // Повторное удаление — тоже 204.
        using (var repeated = await app.Client.DeleteAsync("api/users/me/avatar"))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Public_barber_card_returns_the_owner_avatar_after_approval()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Avatar Public Barber");
        await app.AuthenticateAsync(barber.User.Email);

        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        using var anonymous = app.CreateClient();

        // До одобрения публичная карточка маскирует кадр, даже если URL уже сохранён.
        using (var pendingResponse = await anonymous.GetAsync($"api/barbers/{barber.Profile.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);

            var pendingCard = await pendingResponse.Content
                .ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
            Assert.NotNull(pendingCard);
            Assert.Null(pendingCard.AvatarUrl);
        }

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(admin, barber.User.Id);

        using var response = await anonymous.GetAsync($"api/barbers/{barber.Profile.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
        Assert.NotNull(card);
        Assert.Equal(avatarUrl, card.AvatarUrl);
        Assert.Equal(barber.User.Id, card.UserId);
    }

    [Fact(Timeout = 180_000)]
    public async Task Profile_update_accepts_the_current_media_url_and_rejects_a_foreign_one()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-profile-update"));
        await app.AuthenticateAsync(user.Email);

        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        // Главная регрессия среза: форма отправляет avatarUrl обратно в PUT profile.
        using (var update = await app.Client.PutAsJsonAsync(
                   $"api/users/{user.Id}/profile",
                   new
                   {
                       firstName = "Integration",
                       lastName = "Renamed",
                       phoneNumber = (string?)null,
                       avatarUrl
                   }))
        {
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        }

        var renamed = await GetMyProfileAsync(app.Client);
        Assert.Equal("Renamed", renamed.LastName);
        Assert.Equal(avatarUrl, renamed.AvatarUrl);

        // Тот же URL — не новая загрузка: статус проверки сохраняется, кадр не уходит в очередь заново.
        Assert.Equal(PhotoModerationStatus.Pending, renamed.AvatarModerationStatus);

        var foreignUrl = UserAvatarMediaUrl.Build(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "png");

        using (var reject = await app.Client.PutAsJsonAsync(
                   $"api/users/{user.Id}/profile",
                   new
                   {
                       firstName = "Integration",
                       lastName = "Changed",
                       phoneNumber = (string?)null,
                       avatarUrl = foreignUrl
                   }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, reject.StatusCode);
            Assert.Contains(
                "user.updateProfile.avatarUrl",
                await reject.Content.ReadAsStringAsync());
        }

        var unchanged = await GetMyProfileAsync(app.Client);
        Assert.Equal("Renamed", unchanged.LastName);
        Assert.Equal(avatarUrl, unchanged.AvatarUrl);
    }

    [Fact(Timeout = 180_000)]
    public async Task Repository_returns_avatar_url_null_and_not_found()
    {
        await using var app = await TestApp.StartAsync(_database);
        var owner = await app.CreateUserAsync(NewEmail("avatar-repository"));
        var withoutAvatar = await app.CreateUserAsync(NewEmail("avatar-repository-empty"));
        await app.AuthenticateAsync(owner.Email);

        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        await using var scope = app.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var withAvatar = await repository.GetAvatarAccessAsync(owner.Id, CancellationToken.None);
        var empty = await repository.GetAvatarAccessAsync(withoutAvatar.Id, CancellationToken.None);
        var unknown = await repository.GetAvatarAccessAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.True(withAvatar.IsSuccess);
        Assert.Equal(avatarUrl, withAvatar.Value.AvatarUrl);
        Assert.False(withAvatar.Value.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Pending, withAvatar.Value.ModerationStatus);

        Assert.True(empty.IsSuccess);
        Assert.Null(empty.Value.AvatarUrl);
        Assert.False(empty.Value.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Approved, empty.Value.ModerationStatus);

        Assert.True(unknown.IsFailure);
        Assert.Equal("avatarMedia.notFound", Assert.Single(unknown.Error.Errors).Code);
    }

    [Fact(Timeout = 180_000)]
    public async Task Blocked_avatar_is_not_served_to_anonymous_or_owner_but_admin_gets_private_no_store()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-blocked-media"));

        await app.AuthenticateAsync(user.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        await using var adminSession = await AvatarModerationApi.StartAdminSessionAsync(app);

        // Блокировка имеет смысл только для одобренного кадра: сначала одобряем.
        await AvatarModerationApi.ApproveAsync(adminSession, user.Id);
        await AvatarModerationApi.BlockAsync(adminSession, user.Id);

        // Аноним и владелец получают тот же 404, что и при отсутствии файла: существование
        // скрытого аватара не раскрывается, отдельного 403/410 нет.
        using var anonymous = app.CreateClient();

        using (var anonymousMedia = await anonymous.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        await using (var ownerSession = await app.SignInWithCookieAsync(user.Email))
        using (var ownerMedia = await ownerSession.Client.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, ownerMedia.StatusCode);
        }

        using (var adminMedia = await adminSession.Client.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.OK, adminMedia.StatusCode);

            AssertPrivateNoStore(adminMedia);
            Assert.Equal(
                "nosniff",
                Assert.Single(adminMedia.Headers.GetValues("X-Content-Type-Options")));
        }

        // Возврат отдаёт тот же файл и прежний публичный суточный кэш: статус остался Approved.
        await AvatarModerationApi.UnblockAsync(adminSession, user.Id);

        var restored = await GetMyProfileAsync(app.Client);
        Assert.Equal(PhotoModerationStatus.Approved, restored.AvatarModerationStatus);
        Assert.False(restored.IsAvatarBlocked);

        using (var visible = await anonymous.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            AssertPublicCache(visible);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task My_profile_returns_raw_avatar_and_flag_and_saving_name_does_not_unblock()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-blocked-profile"));

        await app.AuthenticateAsync(user.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        await using var adminSession = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(adminSession, user.Id);
        await AvatarModerationApi.BlockAsync(adminSession, user.Id);

        // Асимметрия users/me: сырой URL + флаг. null здесь стёр бы фото при сохранении имени.
        var blocked = await GetMyProfileAsync(app.Client);

        Assert.Equal(avatarUrl, blocked.AvatarUrl);
        Assert.True(blocked.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Approved, blocked.AvatarModerationStatus);

        // Форма кабинета отправляет текущий avatarUrl обратно: имя меняется, флаг остаётся.
        using (var update = await app.Client.PutAsJsonAsync(
                   $"api/users/{user.Id}/profile",
                   new
                   {
                       firstName = "Renamed",
                       lastName = "Barber",
                       phoneNumber = (string?)null,
                       avatarUrl
                   }))
        {
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        }

        var afterRename = await GetMyProfileAsync(app.Client);

        Assert.Equal("Renamed", afterRename.FirstName);
        Assert.Equal(avatarUrl, afterRename.AvatarUrl);
        Assert.True(afterRename.IsAvatarBlocked);

        await AvatarModerationApi.UnblockAsync(adminSession, user.Id);

        var restored = await GetMyProfileAsync(app.Client);

        Assert.Equal(avatarUrl, restored.AvatarUrl);
        Assert.False(restored.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Approved, restored.AvatarModerationStatus);
    }

    [Fact(Timeout = 180_000)]
    public async Task Uploading_a_new_avatar_clears_the_block_and_waits_for_a_new_approval()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-reupload"));

        await app.AuthenticateAsync(user.Email);
        var firstUrl = await UploadAvatarAsync(app.Client, PngBytes());

        await using var adminSession = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(adminSession, user.Id);
        await AvatarModerationApi.BlockAsync(adminSession, user.Id);

        var secondUrl = await UploadAvatarAsync(app.Client, PngBytes(width: 600, height: 800));

        Assert.NotEqual(firstUrl, secondUrl);

        var profile = await GetMyProfileAsync(app.Client);

        // Новая загрузка — новый файл: флаг снимается доменом, но кадр снова Pending.
        Assert.Equal(secondUrl, profile.AvatarUrl);
        Assert.False(profile.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Pending, profile.AvatarModerationStatus);

        using var anonymous = app.CreateClient();

        using (var oldMedia = await anonymous.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, oldMedia.StatusCode);
        }

        // Публично новый кадр не виден до решения администратора.
        using (var newAnonymousMedia = await anonymous.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, newAnonymousMedia.StatusCode);
        }

        using (var newOwnerMedia = await app.Client.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.OK, newOwnerMedia.StatusCode);
            AssertPrivateNoStore(newOwnerMedia);
        }

        using (var detailResponse = await adminSession.Client.GetAsync(
                   $"api/admin/users/{user.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

            var detail = await detailResponse.Content.ReadFromJsonAsync<AdminUserDetailResponse>(
                JsonOptions);
            Assert.NotNull(detail);
            Assert.Equal(secondUrl, detail.AvatarUrl);
            Assert.False(detail.IsAvatarBlocked);
            Assert.Equal(PhotoModerationStatus.Pending, detail.AvatarModerationStatus);
        }

        await AvatarModerationApi.ApproveAsync(adminSession, user.Id);

        using (var publishedMedia = await anonymous.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.OK, publishedMedia.StatusCode);
            AssertPublicCache(publishedMedia);
        }

        var approvedDetail = await GetMyProfileAsync(app.Client);
        Assert.Equal(PhotoModerationStatus.Approved, approvedDetail.AvatarModerationStatus);
        Assert.Null(approvedDetail.AvatarRejectionReason);
    }

    /// <summary>
    /// Одобренный аватар отдаёт WebP-вариант с честным кэшем, сильным ETag и <c>304</c>
    /// на повторный условный запрос; неизвестный размер — <c>404</c> + <c>no-store</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Approved_avatar_variant_serves_webp_with_honest_cache_etag_and_not_modified()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-variant"));
        await app.AuthenticateAsync(user.Email);

        // Настоящий JPEG: минимальный PNG декодер не примет, и вариант не появится.
        var avatarUrl = await UploadAvatarAsync(
            app.Client,
            MediaTestImage.Jpeg(512, 512),
            fileName: "avatar.jpg",
            contentType: "image/jpeg");

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(admin, user.Id);

        var mediaId = MediaIdFromUrl(avatarUrl);
        var smallUrl = avatarUrl + "?size=sm";

        using var anonymous = app.CreateClient();
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
                    512,
                    512,
                    PhotoVariantWidths.AVATAR_SMALL),
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
            AssertNoVary(notModified);
        }

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

        // Средний и большой варианты — свои ширины и суффиксы ETag.
        foreach (var (suffix, width) in new[]
                 {
                     ("md", PhotoVariantWidths.AVATAR_MEDIUM),
                     ("lg", PhotoVariantWidths.AVATAR_LARGE)
                 })
        {
            using var variant = await anonymous.GetAsync($"{avatarUrl}?size={suffix}");

            Assert.Equal(HttpStatusCode.OK, variant.StatusCode);
            Assert.Equal("image/webp", variant.Content.Headers.ContentType?.MediaType);
            Assert.Equal($"\"{mediaId:N}-{suffix}\"", RequireEntityTag(variant));
            AssertPublicCache(variant);

            var dimensions = MediaTestImage.Dimensions(
                await variant.Content.ReadAsByteArrayAsync());
            Assert.Equal(
                MediaTestImage.VariantDimensions(512, 512, width),
                dimensions);
        }

        foreach (var query in new[] { "?size=xx", "?size=", "?size=sm&size=md" })
        {
            using var invalid = await anonymous.GetAsync(avatarUrl + query);

            Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
            Assert.Contains(
                "avatarMedia.notFound",
                await invalid.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            AssertNoStore(invalid);
            AssertNoVary(invalid);
        }
    }

    /// <summary>
    /// Неодобренный аватар виден владельцу как превью (<c>private, no-store</c>), а после
    /// блокировки не отдаётся даже ему; администратор видит файл всегда.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Pending_and_blocked_avatar_variants_follow_the_visibility_rules()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-variant-visibility"));
        await app.AuthenticateAsync(user.Email);

        var avatarUrl = await UploadAvatarAsync(
            app.Client,
            MediaTestImage.Jpeg(512, 512),
            fileName: "avatar.jpg",
            contentType: "image/jpeg");

        var mediaId = MediaIdFromUrl(avatarUrl);
        var variantUrl = avatarUrl + "?size=md";

        using var anonymous = app.CreateClient();

        using (var pendingAnonymous = await anonymous.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, pendingAnonymous.StatusCode);
            Assert.Contains(
                "avatarMedia.notFound",
                await pendingAnonymous.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            AssertNoStore(pendingAnonymous);
        }

        using (var pendingOwner = await app.Client.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.OK, pendingOwner.StatusCode);
            Assert.Equal("image/webp", pendingOwner.Content.Headers.ContentType?.MediaType);
            AssertPrivateNoStore(pendingOwner);
            Assert.Equal($"\"{mediaId:N}-md\"", RequireEntityTag(pendingOwner));
        }

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(admin, user.Id);

        using (var approvedAnonymous = await anonymous.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.OK, approvedAnonymous.StatusCode);
            Assert.Equal("image/webp", approvedAnonymous.Content.Headers.ContentType?.MediaType);
            AssertPublicCache(approvedAnonymous);
        }

        await AvatarModerationApi.BlockAsync(admin, user.Id);

        // Блокировка сильнее владения: владельцу тот же 404, администратору — превью.
        using (var blockedAnonymous = await anonymous.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, blockedAnonymous.StatusCode);
            AssertNoStore(blockedAnonymous);
        }

        using (var blockedOwner = await app.Client.GetAsync(variantUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, blockedOwner.StatusCode);
            AssertNoStore(blockedOwner);
        }

        using var blockedAdmin = await admin.Client.GetAsync(variantUrl);
        Assert.Equal(HttpStatusCode.OK, blockedAdmin.StatusCode);
        Assert.Equal("image/webp", blockedAdmin.Content.Headers.ContentType?.MediaType);
        AssertPrivateNoStore(blockedAdmin);
    }

    /// <summary>
    /// Замена и удаление аватара убирают исходник и все варианты: старые <c>?size=</c>-URL
    /// сразу отвечают <c>404</c>, а не отдают файл из хранилища.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Replacing_and_deleting_avatar_remove_variants_and_old_size_urls()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(NewEmail("avatar-variant-replace"));
        await app.AuthenticateAsync(user.Email);

        var firstUrl = await UploadAvatarAsync(
            app.Client,
            MediaTestImage.Jpeg(512, 512),
            fileName: "avatar.jpg",
            contentType: "image/jpeg");

        var firstMediaId = MediaIdFromUrl(firstUrl);
        var firstFileName = firstUrl[(firstUrl.LastIndexOf('/') + 1)..];
        var firstPath = Path.Combine(
            app.StorageRootPath,
            "avatars",
            $"{user.Id:N}",
            firstFileName);

        Assert.True(File.Exists(firstPath));
        Assert.Equal(3, VariantFiles(app, user.Id, firstMediaId).Length);

        var secondUrl = await UploadAvatarAsync(
            app.Client,
            MediaTestImage.Jpeg(512, 512),
            fileName: "avatar.jpg",
            contentType: "image/jpeg");

        Assert.NotEqual(firstUrl, secondUrl);

        // Старый кадр заменён: ни исходника, ни вариантов, ни рабочего ?size=.
        Assert.False(File.Exists(firstPath));
        Assert.Empty(VariantFiles(app, user.Id, firstMediaId));

        using var anonymous = app.CreateClient();

        using (var stale = await anonymous.GetAsync(firstUrl + "?size=sm"))
        {
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
            Assert.Contains(
                "avatarMedia.notFound",
                await stale.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            AssertNoStore(stale);
        }

        // Новый кадр ещё ждёт проверки: владелец видит превью с private/no-store.
        var secondMediaId = MediaIdFromUrl(secondUrl);

        using (var pendingVariant = await app.Client.GetAsync(secondUrl + "?size=sm"))
        {
            Assert.Equal(HttpStatusCode.OK, pendingVariant.StatusCode);
            Assert.Equal("image/webp", pendingVariant.Content.Headers.ContentType?.MediaType);
            AssertPrivateNoStore(pendingVariant);
            Assert.Equal($"\"{secondMediaId:N}-sm\"", RequireEntityTag(pendingVariant));
        }

        using (var delete = await app.Client.DeleteAsync("api/users/me/avatar"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.Empty(VariantFiles(app, user.Id, secondMediaId));

        using var deleted = await anonymous.GetAsync(secondUrl + "?size=sm");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        AssertNoStore(deleted);
    }

    /// <summary>
    /// POST multipart с одним файлом в поле <c>file</c>; статус проверяет вызывающий.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAvatarAsync(
        HttpClient client,
        byte[] content,
        string fileName = "avatar.png",
        string contentType = "image/png")
    {
        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        return await client.PostAsync("api/users/me/avatar", form);
    }

    /// <summary>
    /// Загрузка с проверкой <c>201</c>: возвращает относительный URL нового аватара.
    /// </summary>
    private static async Task<string> UploadAvatarAsync(
        HttpClient client,
        byte[] content,
        string fileName = "avatar.png",
        string contentType = "image/png")
    {
        using var response = await SendAvatarAsync(
            client,
            content,
            fileName: fileName,
            contentType: contentType);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AvatarUploadResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AvatarUrl));

        // Любая загрузка — новый кадр на проверку; постмодерации больше нет.
        Assert.Equal(PhotoModerationStatus.Pending, body.ModerationStatus);

        return body.AvatarUrl;
    }

    /// <summary>
    /// Читает <c>GET api/users/me</c> и проверяет <c>200</c>.
    /// </summary>
    private static async Task<MyProfileResponse> GetMyProfileAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<MyProfileResponse>(JsonOptions);
        Assert.NotNull(profile);

        return profile;
    }

    private static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@barbersearch.test";

    /// <summary>
    /// Проверяет запрет кэширования непубличного кадра (pending у владельца, blocked у админа).
    /// </summary>
    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl
            ?? throw new InvalidOperationException("Cache-Control header is missing.");

        Assert.True(cacheControl.Private);
        Assert.True(cacheControl.NoStore);
        Assert.Null(cacheControl.MaxAge);
    }

    /// <summary>
    /// Проверяет честный кэш публичного аватара: <c>public, max-age=0, must-revalidate</c>.
    /// </summary>
    /// <remarks>
    /// Суточного <c>max-age</c> больше нет: скрытый или заменённый аватар обязан исчезать
    /// после перезагрузки, а повторный показ даёт <c>304</c>. Директивы проверяются флагами:
    /// <see cref="HttpResponseMessage.Headers"/> сериализует их в своём порядке.
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
    /// Идентификатор медиа из URL аватара: из него собираются имена вариантов и <c>ETag</c>.
    /// </summary>
    private static Guid MediaIdFromUrl(string url)
    {
        if (!UserAvatarMediaUrl.TryParse(url, out _, out var mediaId, out _))
        {
            throw new InvalidOperationException($"Unexpected avatar media URL: {url}");
        }

        return mediaId;
    }

    /// <summary>
    /// Файлы вариантов аватара на диске теста.
    /// </summary>
    private static string[] VariantFiles(TestApp app, Guid userId, Guid mediaId)
    {
        var directoryPath = Path.Combine(app.StorageRootPath, "avatars", $"{userId:N}");

        return Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, PhotoVariantWidths.FilePattern(mediaId))
            : [];
    }

    /// <summary>
    /// Путь производного файла: <c>{mediaId:N}-{suffix}.webp</c> рядом с исходником.
    /// </summary>
    private static string VariantPath(TestApp app, Guid userId, Guid mediaId, string suffix)
        => Path.Combine(
            app.StorageRootPath,
            "avatars",
            $"{userId:N}",
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
}
