using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Премодерация фотографий на реальной PostgreSQL (issue-89): новый аватар и работа
/// портфолио не видны публично до решения администратора, очередь показывает только
/// <c>Pending</c>, владелец видит статус и причину отказа, а медиа раздаётся по правилам
/// «публичное — всем, непубличное — владельцу и администратору».
/// </summary>
/// <remarks>
/// <para>
/// Каждый тест поднимает собственный хост (<see cref="TestApp.StartAsync"/>): файловое
/// хранилище — уникальный временный каталог, rate limiter — свой на инстанс, база
/// очищается фикстурой. Загрузки и решения идут настоящими HTTP-запросами: очереди и
/// раздача читают БД, а не подставленные в память объекты.
/// </para>
/// <para>
/// Решения администратора отправляются cookie-сессией с <c>X-CSRF-TOKEN</c>, как браузер:
/// это проверяет не только доменное правило, но и глобальный antiforgery-фильтр. Мутации
/// владельца (загрузка, публикация) идут Bearer-клиентом, как в соседних спеках.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PhotoModerationTests
{
    /// <summary>
    /// Сервер отдаёт перечисления (статус модерации, роль, грейд) строками.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public PhotoModerationTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Аватар_ждёт_проверки_и_становится_публичным_только_после_одобрения()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync(
            "Pending Avatar Barber",
            city: "Photo Moderation Avatar City");

        await app.AuthenticateAsync(barber.User.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client);

        using var anonymous = app.CreateClient();

        // Новый кадр не виден ни в публичной карточке, ни в поиске.
        var pendingCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.Null(pendingCard.AvatarUrl);

        var pendingSearch = await GetSearchPageAsync(anonymous, "Photo Moderation Avatar City");
        Assert.Null(Assert.Single(pendingSearch.Items).AvatarUrl);

        // Владелец видит сырой URL и статус «ждёт проверки».
        var pendingProfile = await GetMyProfileAsync(app.Client);
        Assert.Equal(avatarUrl, pendingProfile.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Pending, pendingProfile.AvatarModerationStatus);
        Assert.Null(pendingProfile.AvatarRejectionReason);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        // Очередь «Аватары»: поиск по email владельца, только Pending, сырой URL.
        var queue = await GetQueueAsync<AdminAvatarPhotoResponse>(
            admin.Client,
            $"api/admin/photos/avatars?search={Uri.EscapeDataString(barber.User.Email)}&pageSize=100");
        var row = Assert.Single(queue.Items);

        Assert.Equal(barber.User.Id, row.UserId);
        Assert.Equal(avatarUrl, row.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Pending, row.ModerationStatus);
        Assert.Equal(barber.User.Email, row.Email, ignoreCase: true);
        Assert.NotEqual(default, row.SubmittedAt);

        // Непубличный кадр: анониму 404, владельцу и администратору — 200 без публичного кэша.
        using (var anonymousMedia = await anonymous.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        using (var ownerMedia = await app.Client.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
        }

        using (var adminMedia = await admin.Client.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.OK, adminMedia.StatusCode);
            AssertPrivateNoStore(adminMedia);
        }

        using (var approval = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/admin/users/{barber.User.Id}/avatar/approval"))
        {
            Assert.Equal(HttpStatusCode.NoContent, approval.StatusCode);
        }

        // Одобренный кадр уходит из очереди.
        var emptyQueue = await GetQueueAsync<AdminAvatarPhotoResponse>(
            admin.Client,
            $"api/admin/photos/avatars?search={Uri.EscapeDataString(barber.User.Email)}&pageSize=100");
        Assert.Empty(emptyQueue.Items);

        // И появляется на публичных поверхностях; владелец видит Approved.
        var approvedCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.Equal(avatarUrl, approvedCard.AvatarUrl);

        var approvedSearch = await GetSearchPageAsync(anonymous, "Photo Moderation Avatar City");
        Assert.Equal(avatarUrl, Assert.Single(approvedSearch.Items).AvatarUrl);

        var approvedProfile = await GetMyProfileAsync(app.Client);
        Assert.Equal(PhotoModerationStatus.Approved, approvedProfile.AvatarModerationStatus);
        Assert.Null(approvedProfile.AvatarRejectionReason);

        using var publicMedia = await anonymous.GetAsync(avatarUrl);
        Assert.Equal(HttpStatusCode.OK, publicMedia.StatusCode);
        AssertPublicCache(publicMedia);
    }

    [Fact(Timeout = 180_000)]
    public async Task Очередь_аватаров_держит_FIFO_пагинацию_поиск_и_валидирует_параметры()
    {
        await using var app = await TestApp.StartAsync(_database);

        var users = new List<User>();

        for (var index = 0; index < 3; index++)
        {
            var user = await app.CreateUserAsync(
                $"photo-fifo-{index}-{Guid.NewGuid():N}@barbersearch.test",
                UserRole.Barber);

            await app.AuthenticateAsync(user.Email);
            await UploadAvatarAsync(app.Client);

            users.Add(user);
        }

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        const string search = "photo-fifo-";

        var firstPage = await GetQueueAsync<AdminAvatarPhotoResponse>(
            admin.Client,
            $"api/admin/photos/avatars?search={search}&pageSize=2");

        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(users[0].Id, firstPage.Items[0].UserId);
        Assert.Equal(users[1].Id, firstPage.Items[1].UserId);
        Assert.True(firstPage.Items[0].SubmittedAt <= firstPage.Items[1].SubmittedAt);

        var secondPage = await GetQueueAsync<AdminAvatarPhotoResponse>(
            admin.Client,
            $"api/admin/photos/avatars?search={search}&pageSize=2"
            + $"&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(users[2].Id, secondPage.Items[0].UserId);

        // Поиск по email сужает очередь до владельца одного кадра.
        var byEmail = await GetQueueAsync<AdminAvatarPhotoResponse>(
            admin.Client,
            $"api/admin/photos/avatars?search={Uri.EscapeDataString(users[1].Email)}&pageSize=100");
        Assert.Equal(users[1].Id, Assert.Single(byEmail.Items).UserId);

        // Коды ошибок параметров — часть контракта, одинаковые для обеих вкладок.
        foreach (var path in new[] { "api/admin/photos/avatars", "api/admin/photos/portfolio" })
        {
            using (var brokenCursor = await admin.Client.GetAsync($"{path}?cursor=not-a-cursor"))
            {
                await AssertErrorCodeAsync(
                    brokenCursor,
                    HttpStatusCode.BadRequest,
                    "admin.photos.cursor");
            }

            using (var longSearch = await admin.Client.GetAsync(
                       $"{path}?search={new string('x', 101)}"))
            {
                await AssertErrorCodeAsync(
                    longSearch,
                    HttpStatusCode.BadRequest,
                    "admin.photos.search");
            }

            using (var zeroPage = await admin.Client.GetAsync($"{path}?pageSize=0"))
            {
                await AssertErrorCodeAsync(
                    zeroPage,
                    HttpStatusCode.BadRequest,
                    "admin.photos.pageSize");
            }

            using (var hugePage = await admin.Client.GetAsync($"{path}?pageSize=101"))
            {
                await AssertErrorCodeAsync(
                    hugePage,
                    HttpStatusCode.BadRequest,
                    "admin.photos.pageSize");
            }
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Отклонённый_аватар_показывает_причину_и_не_решается_повторно()
    {
        await using var app = await TestApp.StartAsync(_database);

        var owner = await app.CreateUserAsync(NewEmail("photo-reject-owner"));
        await app.AuthenticateAsync(owner.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        // Причина тримится доменом: владелец видит нормализованный текст.
        using (var rejection = await SendAvatarRejectionAsync(admin, owner.Id, "  Размыто  "))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejection.StatusCode);
        }

        var rejected = await GetMyProfileAsync(app.Client);
        Assert.Equal(avatarUrl, rejected.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Rejected, rejected.AvatarModerationStatus);
        Assert.Equal("Размыто", rejected.AvatarRejectionReason);

        // Публично кадр не виден, владелец видит превью с private/no-store.
        using var anonymous = app.CreateClient();

        using (var anonymousMedia = await anonymous.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        using (var ownerMedia = await app.Client.GetAsync(avatarUrl))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
        }

        // Повторное решение по проверенному кадру — 409, не 204.
        using (var repeatApproval = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/admin/users/{owner.Id}/avatar/approval"))
        {
            await AssertErrorCodeAsync(
                repeatApproval,
                HttpStatusCode.Conflict,
                "user.avatarModeration.invalidStatus");
        }

        using (var repeatRejection = await SendAvatarRejectionAsync(admin, owner.Id, "Ещё раз"))
        {
            await AssertErrorCodeAsync(
                repeatRejection,
                HttpStatusCode.Conflict,
                "user.avatarModeration.invalidStatus");
        }

        // У пользователя без аватара проверять нечего: 409 noAvatar и на одобрение, и на отказ.
        var withoutAvatar = await app.CreateUserAsync(NewEmail("photo-reject-no-avatar"));

        using (var noAvatarApproval = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/admin/users/{withoutAvatar.Id}/avatar/approval"))
        {
            await AssertErrorCodeAsync(
                noAvatarApproval,
                HttpStatusCode.Conflict,
                "user.avatarModeration.noAvatar");
        }

        using (var noAvatarRejection = await SendAvatarRejectionAsync(
                   admin,
                   withoutAvatar.Id,
                   "Причина"))
        {
            await AssertErrorCodeAsync(
                noAvatarRejection,
                HttpStatusCode.Conflict,
                "user.avatarModeration.noAvatar");
        }

        // Валидация причины: пустая/из пробелов — 400 с invalidField = "reason",
        // 501 символ — 400 reasonTooLong, ровно 500 — успех.
        var validation = await app.CreateUserAsync(NewEmail("photo-reject-validation"));
        await app.AuthenticateAsync(validation.Email);
        await UploadAvatarAsync(app.Client);

        using (var emptyReason = await SendAvatarRejectionAsync(admin, validation.Id, string.Empty))
        {
            await AssertReasonValidationAsync(
                emptyReason,
                "user.avatarModeration.reasonRequired");
        }

        using (var blankReason = await SendAvatarRejectionAsync(admin, validation.Id, "   "))
        {
            await AssertReasonValidationAsync(
                blankReason,
                "user.avatarModeration.reasonRequired");
        }

        using (var tooLong = await SendAvatarRejectionAsync(
                   admin,
                   validation.Id,
                   new string('а', 501)))
        {
            await AssertReasonValidationAsync(
                tooLong,
                "user.avatarModeration.reasonTooLong");
        }

        // Неудачная валидация не меняет статус: кадр всё ещё Pending и решается.
        Assert.Equal(
            PhotoModerationStatus.Pending,
            (await GetMyProfileAsync(app.Client)).AvatarModerationStatus);

        using (var boundary = await SendAvatarRejectionAsync(
                   admin,
                   validation.Id,
                   new string('б', 500)))
        {
            Assert.Equal(HttpStatusCode.NoContent, boundary.StatusCode);
        }

        var boundaryRejected = await GetMyProfileAsync(app.Client);
        Assert.Equal(PhotoModerationStatus.Rejected, boundaryRejected.AvatarModerationStatus);
        Assert.Equal(500, boundaryRejected.AvatarRejectionReason!.Length);
    }

    [Fact(Timeout = 180_000)]
    public async Task Работа_портфолио_ждёт_проверки_и_становится_публичной_после_одобрения()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Pending Portfolio Barber");

        await app.AuthenticateAsync(barber.User.Email);
        var photoId = await UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Pending haircut");

        using var anonymous = app.CreateClient();

        // Публичная галерея пуста, собственная карточка показывает Pending.
        var pendingPublicCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.Empty(pendingPublicCard.Photos);

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var pendingPhoto = Assert.Single(ownCard.Photos);

        Assert.Equal(photoId, pendingPhoto.Id);
        Assert.Equal(PhotoModerationStatus.Pending, pendingPhoto.ModerationStatus);
        Assert.Null(pendingPhoto.RejectionReason);
        Assert.Equal("Pending haircut", pendingPhoto.Description);

        using (var anonymousMedia = await anonymous.GetAsync(pendingPhoto.Url))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        using (var ownerMedia = await app.Client.GetAsync(pendingPhoto.Url))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
        }

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        // Очередь «Портфолио»: поиск по имени барбера, только Pending.
        var queue = await GetQueueAsync<AdminPortfolioPhotoResponse>(
            admin.Client,
            $"api/admin/photos/portfolio?search={Uri.EscapeDataString("Pending Portfolio Barber")}&pageSize=100");
        var row = Assert.Single(queue.Items);

        Assert.Equal(photoId, row.PhotoId);
        Assert.Equal(barber.Profile.Id, row.BarberProfileId);
        Assert.Equal(pendingPhoto.Url, row.Url);
        Assert.Equal("Pending haircut", row.Description);
        Assert.True(row.IsPublished);
        Assert.Equal(PhotoModerationStatus.Pending, row.ModerationStatus);
        Assert.Equal(barber.User.Email, row.OwnerEmail, ignoreCase: true);
        Assert.NotEqual(default, row.SubmittedAt);

        await ApprovePortfolioAsync(admin, barber.Profile.Id, photoId);

        Assert.Empty((await GetQueueAsync<AdminPortfolioPhotoResponse>(
            admin.Client,
            $"api/admin/photos/portfolio?search={Uri.EscapeDataString("Pending Portfolio Barber")}&pageSize=100")).Items);

        // Одобренный кадр появляется публично с Approved и без причины.
        var publicCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        var publicPhoto = Assert.Single(publicCard.Photos);

        Assert.Equal(photoId, publicPhoto.Id);
        Assert.Equal(PhotoModerationStatus.Approved, publicPhoto.ModerationStatus);
        Assert.Null(publicPhoto.RejectionReason);

        using (var publicMedia = await anonymous.GetAsync(publicPhoto.Url))
        {
            Assert.Equal(HttpStatusCode.OK, publicMedia.StatusCode);
            AssertPublicCache(publicMedia);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Отклонённая_работа_показывает_причину_и_не_решается_повторно()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Rejected Portfolio Barber");

        await app.AuthenticateAsync(barber.User.Email);
        var approvedCandidate = await UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Good photo");
        var rejectedCandidate = await UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Bad photo");

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        using (var rejection = await SendPortfolioRejectionAsync(
                   admin,
                   barber.Profile.Id,
                   rejectedCandidate,
                   "  Обрезано  "))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejection.StatusCode);
        }

        // Владелец видит статус и причину, публично кадр не показывается.
        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var rejectedPhoto = Assert.Single(
            ownCard.Photos,
            photo => photo.Id == rejectedCandidate);

        Assert.Equal(PhotoModerationStatus.Rejected, rejectedPhoto.ModerationStatus);
        Assert.Equal("Обрезано", rejectedPhoto.RejectionReason);

        using var anonymous = app.CreateClient();
        var emptyPublicCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.Empty(emptyPublicCard.Photos);

        // Первый кадр одобряем — публично виден только он.
        await ApprovePortfolioAsync(admin, barber.Profile.Id, approvedCandidate);

        var publicCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        var visiblePhoto = Assert.Single(publicCard.Photos);

        Assert.Equal(approvedCandidate, visiblePhoto.Id);
        Assert.Equal(PhotoModerationStatus.Approved, visiblePhoto.ModerationStatus);
        Assert.DoesNotContain(publicCard.Photos, photo => photo.Id == rejectedCandidate);

        // Повторное решение по отклонённому кадру — 409, не 204.
        using (var repeatRejection = await SendPortfolioRejectionAsync(
                   admin,
                   barber.Profile.Id,
                   rejectedCandidate,
                   "Ещё раз"))
        {
            await AssertErrorCodeAsync(
                repeatRejection,
                HttpStatusCode.Conflict,
                "portfolioPhoto.moderation.invalidStatus");
        }

        using (var repeatApproval = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{rejectedCandidate}/approval"))
        {
            await AssertErrorCodeAsync(
                repeatApproval,
                HttpStatusCode.Conflict,
                "portfolioPhoto.moderation.invalidStatus");
        }

        // Неизвестная фотография в существующем профиле и неизвестный профиль — разные 404.
        using (var unknownPhoto = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{Guid.CreateVersion7()}/approval"))
        {
            await AssertErrorCodeAsync(
                unknownPhoto,
                HttpStatusCode.NotFound,
                "portfolioPhoto.moderation.notFound");
        }

        using (var unknownProfile = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/barber-profiles/{Guid.CreateVersion7()}/photos/{approvedCandidate}/approval"))
        {
            await AssertErrorCodeAsync(
                unknownProfile,
                HttpStatusCode.NotFound,
                "barberProfile.notFound");
        }

        // Валидация причины на третьем Pending-кадре: пустая и слишком длинная — 400.
        var validationPhoto = await UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Validation photo");

        using (var emptyReason = await SendPortfolioRejectionAsync(
                   admin,
                   barber.Profile.Id,
                   validationPhoto,
                   string.Empty))
        {
            await AssertReasonValidationAsync(
                emptyReason,
                "portfolioPhoto.moderation.reasonRequired");
        }

        using (var tooLong = await SendPortfolioRejectionAsync(
                   admin,
                   barber.Profile.Id,
                   validationPhoto,
                   new string('в', 501)))
        {
            await AssertReasonValidationAsync(
                tooLong,
                "portfolioPhoto.moderation.reasonTooLong");
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Очереди_и_решения_фотографий_закрыты_для_анонима_и_не_администратора()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Authz Photo Barber");

        await app.AuthenticateAsync(barber.User.Email);
        await UploadAvatarAsync(app.Client);
        var photoId = await UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        var avatarApproval = $"api/admin/users/{barber.User.Id}/avatar/approval";
        var avatarRejection = $"api/admin/users/{barber.User.Id}/avatar/rejection";
        var portfolioApproval =
            $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/approval";
        var portfolioRejection =
            $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/rejection";

        var requests = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "api/admin/photos/avatars", null),
            (HttpMethod.Get, "api/admin/photos/portfolio", null),
            (HttpMethod.Post, avatarApproval, null),
            (HttpMethod.Post, avatarRejection, new { reason = "Нет" }),
            (HttpMethod.Post, portfolioApproval, null),
            (HttpMethod.Post, portfolioRejection, new { reason = "Нет" })
        };

        // Аноним: 401 до проверки антифоргерии, как у остальных cookie-мутаций.
        using (var anonymous = app.CreateClient())
        {
            foreach (var (method, path, body) in requests)
            {
                using var request = new HttpRequestMessage(method, path);

                if (body is not null)
                {
                    request.Content = JsonContent.Create(body);
                }

                using var response = await anonymous.SendAsync(request);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        // Клиент и барбер — не администраторы: 403 и на чтения, и на все четыре решения.
        var customer = await app.CreateUserAsync(NewEmail("photo-authz-customer"));

        await using var customerSession = await app.SignInWithCookieAsync(customer.Email);

        foreach (var (method, path, body) in requests)
        {
            using var response = await SendWithCsrfAsync(customerSession, method, path, body);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Администратор без CSRF-заголовка не проходит общий фильтр, и кадр остаётся Pending.
        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        using (var withoutCsrf = await admin.Client.PostAsync(avatarApproval, content: null))
        {
            await AssertErrorCodeAsync(
                withoutCsrf,
                HttpStatusCode.BadRequest,
                "auth.csrf.invalid");
        }

        Assert.Equal(
            PhotoModerationStatus.Pending,
            (await GetMyProfileAsync(app.Client)).AvatarModerationStatus);

        using (var withCsrf = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   avatarApproval))
        {
            Assert.Equal(HttpStatusCode.NoContent, withCsrf.StatusCode);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Блокировка_одобренного_аватара_скрывает_кадр_а_отклонённый_остаётся_отклонённым()
    {
        await using var app = await TestApp.StartAsync(_database);

        var approvedOwner = await app.CreateUserAsync(NewEmail("photo-block-approved"));
        await app.AuthenticateAsync(approvedOwner.Email);
        var approvedUrl = await UploadAvatarAsync(app.Client);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(admin, approvedOwner.Id);

        using var anonymous = app.CreateClient();

        using (var visible = await anonymous.GetAsync(approvedUrl))
        {
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            AssertPublicCache(visible);
        }

        await AvatarModerationApi.BlockAsync(admin, approvedOwner.Id);

        var blocked = await GetMyProfileAsync(app.Client);
        Assert.Equal(PhotoModerationStatus.Approved, blocked.AvatarModerationStatus);
        Assert.True(blocked.IsAvatarBlocked);

        using (var anonymousMedia = await anonymous.GetAsync(approvedUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        using (var ownerMedia = await app.Client.GetAsync(approvedUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, ownerMedia.StatusCode);
        }

        using (var adminMedia = await admin.Client.GetAsync(approvedUrl))
        {
            Assert.Equal(HttpStatusCode.OK, adminMedia.StatusCode);
            AssertPrivateNoStore(adminMedia);
        }

        // Разблокировка возвращает одобренный кадр, статус модерации не меняется.
        await AvatarModerationApi.UnblockAsync(admin, approvedOwner.Id);

        using (var restored = await anonymous.GetAsync(approvedUrl))
        {
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            AssertPublicCache(restored);
        }

        // Отклонённый кадр блокируется доменом, но не становится одобренным:
        // Blocked — отдельный флаг, Rejected — решение о кадре.
        var rejectedOwner = await app.CreateUserAsync(NewEmail("photo-block-rejected"));
        await app.AuthenticateAsync(rejectedOwner.Email);
        var rejectedUrl = await UploadAvatarAsync(app.Client);

        using (var rejection = await SendAvatarRejectionAsync(admin, rejectedOwner.Id, "Низкое качество"))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejection.StatusCode);
        }

        await AvatarModerationApi.BlockAsync(admin, rejectedOwner.Id);

        var rejectedBlocked = await GetMyProfileAsync(app.Client);
        Assert.Equal(PhotoModerationStatus.Rejected, rejectedBlocked.AvatarModerationStatus);
        Assert.Equal("Низкое качество", rejectedBlocked.AvatarRejectionReason);
        Assert.True(rejectedBlocked.IsAvatarBlocked);

        using (var ownerPreview = await app.Client.GetAsync(rejectedUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, ownerPreview.StatusCode);
        }

        using (var adminPreview = await admin.Client.GetAsync(rejectedUrl))
        {
            Assert.Equal(HttpStatusCode.OK, adminPreview.StatusCode);
            AssertPrivateNoStore(adminPreview);
        }

        await AvatarModerationApi.UnblockAsync(admin, rejectedOwner.Id);

        // Возврат не делает кадр публичным: он по-прежнему Rejected.
        using (var stillHidden = await anonymous.GetAsync(rejectedUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, stillHidden.StatusCode);
        }

        var restoredRejected = await GetMyProfileAsync(app.Client);
        Assert.Equal(PhotoModerationStatus.Rejected, restoredRejected.AvatarModerationStatus);
        Assert.False(restoredRejected.IsAvatarBlocked);
    }

    [Fact(Timeout = 180_000)]
    public async Task Блокировка_одобренной_работы_скрывает_её_а_отклонённая_остаётся_отклонённой()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Block Portfolio Barber");

        await app.AuthenticateAsync(barber.User.Email);
        var approvedPhoto = await UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await ApprovePortfolioAsync(admin, barber.Profile.Id, approvedPhoto);

        using var anonymous = app.CreateClient();

        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var url = Assert.Single(ownCard.Photos).Url;

        var blockUrl = $"api/barber-profiles/{barber.Profile.Id}/photos/{approvedPhoto}/block";

        using (var block = await AvatarModerationApi.SendAdminAsync(admin, HttpMethod.Post, blockUrl))
        {
            Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        }

        var hiddenCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.Empty(hiddenCard.Photos);

        using (var anonymousMedia = await anonymous.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        using (var ownerMedia = await app.Client.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
        }

        using (var unblock = await AvatarModerationApi.SendAdminAsync(admin, HttpMethod.Delete, blockUrl))
        {
            Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        }

        var restoredCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        var restored = Assert.Single(restoredCard.Photos);
        Assert.Equal(PhotoModerationStatus.Approved, restored.ModerationStatus);

        // Отклонённый кадр: блокировка не превращает Rejected в Approved.
        var rejectedPhoto = await UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        using (var rejection = await SendPortfolioRejectionAsync(
                   admin,
                   barber.Profile.Id,
                   rejectedPhoto,
                   "Не работа"))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejection.StatusCode);
        }

        using (var blockRejected = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{rejectedPhoto}/block"))
        {
            Assert.Equal(HttpStatusCode.NoContent, blockRejected.StatusCode);
        }

        var ownAfterBlock = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var rejectedBlocked = Assert.Single(
            ownAfterBlock.Photos,
            photo => photo.Id == rejectedPhoto);

        Assert.Equal(PhotoModerationStatus.Rejected, rejectedBlocked.ModerationStatus);
        Assert.Equal("Не работа", rejectedBlocked.RejectionReason);
        Assert.True(rejectedBlocked.IsBlocked);

        using (var stillHidden = await anonymous.GetAsync(rejectedBlocked.Url))
        {
            Assert.Equal(HttpStatusCode.NotFound, stillHidden.StatusCode);
        }

        using (var unblockRejected = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Delete,
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{rejectedPhoto}/block"))
        {
            Assert.Equal(HttpStatusCode.NoContent, unblockRejected.StatusCode);
        }

        var ownAfterUnblock = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var stillRejected = Assert.Single(
            ownAfterUnblock.Photos,
            photo => photo.Id == rejectedPhoto);

        Assert.Equal(PhotoModerationStatus.Rejected, stillRejected.ModerationStatus);
        Assert.False(stillRejected.IsBlocked);

        // Публично отклонённый кадр не появился ни до, ни после блокировки.
        var publicAfterUnblock = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.DoesNotContain(publicAfterUnblock.Photos, photo => photo.Id == rejectedPhoto);
    }

    [Fact(Timeout = 180_000)]
    public async Task Медиа_отдаёт_непубличный_кадр_только_владельцу_и_сравнивает_точный_URL()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Media Photo Barber");

        // Аватар: непубличный кадр — владельцу и администратору, анониму 404.
        await app.AuthenticateAsync(barber.User.Email);
        var firstUrl = await UploadAvatarAsync(app.Client);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        using var anonymous = app.CreateClient();

        using (var anonymousMedia = await anonymous.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, anonymousMedia.StatusCode);
        }

        using (var ownerMedia = await app.Client.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.OK, ownerMedia.StatusCode);
            AssertPrivateNoStore(ownerMedia);
        }

        using (var adminMedia = await admin.Client.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.OK, adminMedia.StatusCode);
            AssertPrivateNoStore(adminMedia);
        }

        // Замена: старый URL перестаёт работать даже у владельца и администратора —
        // сравнение сохранённой строки с запрошенной не ослаблено.
        var secondUrl = await UploadAvatarAsync(app.Client);

        Assert.NotEqual(firstUrl, secondUrl);

        using (var oldAnonymous = await anonymous.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, oldAnonymous.StatusCode);
        }

        using (var oldOwner = await app.Client.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, oldOwner.StatusCode);
        }

        using (var oldAdmin = await admin.Client.GetAsync(firstUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, oldAdmin.StatusCode);
        }

        using (var newOwner = await app.Client.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.OK, newOwner.StatusCode);
            AssertPrivateNoStore(newOwner);
        }

        await AvatarModerationApi.ApproveAsync(admin, barber.User.Id);

        using (var published = await anonymous.GetAsync(secondUrl))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
            AssertPublicCache(published);
        }

        // Портфолио: та же модель — публично только Approved && IsPublished && !IsBlocked.
        var photoId = await UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);
        var ownCard = await GetCardAsync(
            app.Client,
            $"api/barber-profiles/{barber.Profile.Id}");
        var photo = Assert.Single(ownCard.Photos, item => item.Id == photoId);

        using (var portfolioAnonymous = await anonymous.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.NotFound, portfolioAnonymous.StatusCode);
        }

        using (var portfolioOwner = await app.Client.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.OK, portfolioOwner.StatusCode);
            AssertPrivateNoStore(portfolioOwner);
        }

        using (var portfolioAdmin = await admin.Client.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.OK, portfolioAdmin.StatusCode);
            AssertPrivateNoStore(portfolioAdmin);
        }

        await ApprovePortfolioAsync(admin, barber.Profile.Id, photoId);

        using (var portfolioPublic = await anonymous.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.OK, portfolioPublic.StatusCode);
            AssertPublicCache(portfolioPublic);
        }

        // Снятие с публикации владельцем снова прячет кадр, но оставляет его в кабинете.
        using (var unpublish = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/publication"))
        {
            Assert.Equal(HttpStatusCode.NoContent, unpublish.StatusCode);
        }

        using (var hiddenPublic = await anonymous.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.NotFound, hiddenPublic.StatusCode);
        }

        using (var hiddenOwner = await app.Client.GetAsync(photo.Url))
        {
            Assert.Equal(HttpStatusCode.OK, hiddenOwner.StatusCode);
            AssertPrivateNoStore(hiddenOwner);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Решения_и_загрузки_не_добавляют_новых_типов_аудита()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Photo Barber");

        await app.AuthenticateAsync(barber.User.Email);
        await UploadAvatarAsync(app.Client);
        var photoId = await UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        await AvatarModerationApi.ApproveAsync(admin, barber.User.Id);

        using (var rejection = await SendPortfolioRejectionAsync(
                   admin,
                   barber.Profile.Id,
                   photoId,
                   "Не подходит"))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejection.StatusCode);
        }

        // Ни загрузки, ни решения администратора не входят в allowlist: журнал пуст,
        // а новых типов сущностей (портфолио — дочерняя строка) в нём нет.
        Assert.Empty(await data.ReadAuditRowsAsync());
        Assert.Equal(0, await data.CountAuditRowsAsync("portfolio_photo"));
        Assert.Equal(0, await data.CountAuditRowsAsync("user"));
        Assert.Equal(0, await data.CountAuditRowsAsync("barber_profile"));

        // Фильтр админского журнала по-прежнему принимает только старые типы.
        using var unknownType = await admin.Client.GetAsync(
            "api/admin/audit?entityType=portfolio_photo");

        await AssertErrorCodeAsync(
            unknownType,
            HttpStatusCode.BadRequest,
            "admin.audit.entityType");
    }

    [Fact(Timeout = 180_000)]
    public async Task Счётчики_фотографий_в_сводке_равны_размерам_очередей()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        foreach (var index in new[] { 1, 2 })
        {
            var user = await app.CreateUserAsync(
                $"photo-summary-avatar-{index}-{Guid.NewGuid():N}@barbersearch.test",
                UserRole.Barber);

            await app.AuthenticateAsync(user.Email);
            await UploadAvatarAsync(app.Client);
        }

        var barber = await data.CreateBookableBarberAsync("Summary Photo Barber");
        await app.AuthenticateAsync(barber.User.Email);
        var firstPhoto = await UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);
        await UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        var before = await GetSummaryAsync(admin.Client);
        Assert.Equal(2, before.Photos.PendingAvatarsCount);
        Assert.Equal(2, before.Photos.PendingPortfolioCount);

        Assert.Equal(
            before.Photos.PendingAvatarsCount,
            (await GetQueueAsync<AdminAvatarPhotoResponse>(
                admin.Client,
                "api/admin/photos/avatars?pageSize=100")).Items.Count);
        Assert.Equal(
            before.Photos.PendingPortfolioCount,
            (await GetQueueAsync<AdminPortfolioPhotoResponse>(
                admin.Client,
                "api/admin/photos/portfolio?pageSize=100")).Items.Count);

        // Одно решение в каждой вкладке уменьшает и число сводки, и размер очереди.
        var avatarQueue = await GetQueueAsync<AdminAvatarPhotoResponse>(
            admin.Client,
            "api/admin/photos/avatars?pageSize=100");

        await AvatarModerationApi.ApproveAsync(admin, avatarQueue.Items[0].UserId);
        await ApprovePortfolioAsync(admin, barber.Profile.Id, firstPhoto);

        var after = await GetSummaryAsync(admin.Client);
        Assert.Equal(1, after.Photos.PendingAvatarsCount);
        Assert.Equal(1, after.Photos.PendingPortfolioCount);

        Assert.Equal(
            after.Photos.PendingAvatarsCount,
            (await GetQueueAsync<AdminAvatarPhotoResponse>(
                admin.Client,
                "api/admin/photos/avatars?pageSize=100")).Items.Count);
        Assert.Equal(
            after.Photos.PendingPortfolioCount,
            (await GetQueueAsync<AdminPortfolioPhotoResponse>(
                admin.Client,
                "api/admin/photos/portfolio?pageSize=100")).Items.Count);
    }

    [Fact(Timeout = 180_000)]
    public async Task Списки_броней_не_показывают_аватар_клиента_до_одобрения()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Photo Booking Barber");
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(NewEmail("photo-booking-customer"));

        await app.AuthenticateAsync(customer.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(3),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(barber.User.Email);

        using (var pendingResponse = await app.Client.GetAsync("api/barber/bookings"))
        {
            Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);

            var page = await pendingResponse.Content
                .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);

            Assert.NotNull(page);
            Assert.Null(Assert.Single(page.Items, item => item.Id == booking.Id)
                .Customer.AvatarUrl);
        }

        await using (var admin = await AvatarModerationApi.StartAdminSessionAsync(app))
        {
            await AvatarModerationApi.ApproveAsync(admin, customer.Id);
        }

        using (var approvedResponse = await app.Client.GetAsync("api/barber/bookings"))
        {
            Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);

            var page = await approvedResponse.Content
                .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);

            Assert.NotNull(page);
            Assert.Equal(
                avatarUrl,
                Assert.Single(page.Items, item => item.Id == booking.Id)
                    .Customer.AvatarUrl);
        }
    }

    private static async Task<(string Url, AvatarUploadResponse Body)> UploadAvatarWithBodyAsync(
        HttpClient client)
    {
        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(PngBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "avatar.png");

        using var response = await client.PostAsync("api/users/me/avatar", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AvatarUploadResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AvatarUrl));
        Assert.Equal(PhotoModerationStatus.Pending, body.ModerationStatus);

        return (body.AvatarUrl, body);
    }

    private static async Task<string> UploadAvatarAsync(HttpClient client)
        => (await UploadAvatarWithBodyAsync(client)).Url;

    private static async Task<Guid> UploadPortfolioPhotoAsync(
        HttpClient client,
        Guid barberProfileId,
        string? description = null)
    {
        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(PngBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");

        if (description is not null)
        {
            form.Add(new StringContent(description), "description");
        }

        using var response = await client.PostAsync(
            $"api/barber-profiles/{barberProfileId}/photos/upload",
            form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>(JsonOptions);
        Assert.NotNull(body);

        return body.Id;
    }

    private static Task<HttpResponseMessage> SendAvatarRejectionAsync(
        AdminAvatarSession admin,
        Guid userId,
        string? reason)
        => AvatarModerationApi.SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/admin/users/{userId}/avatar/rejection",
            new { reason });

    private static Task<HttpResponseMessage> SendPortfolioRejectionAsync(
        AdminAvatarSession admin,
        Guid barberProfileId,
        Guid photoId,
        string? reason)
        => AvatarModerationApi.SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/rejection",
            new { reason });

    private static async Task ApprovePortfolioAsync(
        AdminAvatarSession admin,
        Guid barberProfileId,
        Guid photoId)
    {
        using var response = await AvatarModerationApi.SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/approval");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<PagedResponse<T>> GetQueueAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<T>>(JsonOptions);
        Assert.NotNull(page);

        return page;
    }

    private static async Task<AdminSummaryResponse> GetSummaryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/admin/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<AdminSummaryResponse>(JsonOptions);
        Assert.NotNull(summary);

        return summary;
    }

    private static async Task<BarberProfileResponse> GetPublicCardAsync(
        HttpClient client,
        Guid profileId)
    {
        using var response = await client.GetAsync($"api/barbers/{profileId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
        Assert.NotNull(card);

        return card;
    }

    private static async Task<BarberProfileResponse> GetCardAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
        Assert.NotNull(card);

        return card;
    }

    private static async Task<PagedResponse<BarberSearchItemResponse>> GetSearchPageAsync(
        HttpClient client,
        string city)
    {
        using var response = await client.GetAsync(
            $"api/barbers?city={Uri.EscapeDataString(city)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BarberSearchItemResponse>>(JsonOptions);
        Assert.NotNull(page);

        return page;
    }

    private static async Task<MyProfileResponse> GetMyProfileAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<MyProfileResponse>(JsonOptions);
        Assert.NotNull(profile);

        return profile;
    }

    private static Task<HttpResponseMessage> SendWithCsrfAsync(
        CookieSession session,
        HttpMethod method,
        string path,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);

        return session.Client.SendAsync(request);
    }

    private static async Task AssertErrorCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Проверяет 400 валидации причины: <c>invalidField = "reason"</c> и публичный код.
    /// </summary>
    private static async Task AssertReasonValidationAsync(
        HttpResponseMessage response,
        string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);

        Assert.True(
            document.RootElement.GetProperty("errors").TryGetProperty("reason", out _),
            $"Validation failure must carry 'errors.reason'; body: {body}");
        Assert.Contains(code, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Проверяет запрет кэширования непубличного кадра.
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
    /// Проверяет публичное суточное кэширование кадра, доступного всем.
    /// </summary>
    private static void AssertPublicCache(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl
            ?? throw new InvalidOperationException("Cache-Control header is missing.");

        Assert.True(cacheControl.Public);
        Assert.Equal(TimeSpan.FromDays(1), cacheControl.MaxAge);
        Assert.False(cacheControl.NoStore);
    }

    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        var linked = user.LinkBarberProfile(barber.Profile.Id);

        if (linked.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {linked.Error.Errors.Select(error => error.Code)}");
        }

        await db.SaveChangesAsync();
    }

    private static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@barbersearch.test";

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
