using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Аватар в трёх чтениях среза #86 на реальной PostgreSQL: карточка поиска, записи барбера
/// и админские брони. Аватар клиента — из левого join'а (<c>null</c> у гостя), аватар
/// барбера — из владельца профиля, а не из клиентского join'а.
/// </summary>
/// <remarks>
/// Каждый тест поднимает собственный хост, поэтому база и файловое хранилище изолированы.
/// Аватар загружается настоящим multipart-запросом: относительный URL проверяется в ответе
/// чтения, а не подставляется в базу напрямую.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AvatarReadSurfacesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public AvatarReadSurfacesTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Search_returns_avatar_null_after_upload_and_null_again_after_delete()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        // Изолированный профиль: город есть только у него, поэтому выдача состоит из одной
        // карточки. Витринный барбер …0001 в интеграционных тестах не создаётся и не трогается.
        var barber = await data.CreateBookableBarberAsync(
            "Avatar Search Barber",
            city: "Avatar Search City");

        using var anonymous = app.CreateClient();

        var before = await GetSearchPageAsync(anonymous, "Avatar Search City");
        var beforeItem = Assert.Single(before.Items);
        Assert.Equal(barber.Profile.Id, beforeItem.Id);
        Assert.Null(beforeItem.AvatarUrl);

        await app.AuthenticateAsync(barber.User.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        var afterUpload = await GetSearchPageAsync(anonymous, "Avatar Search City");
        var withAvatar = Assert.Single(afterUpload.Items);
        Assert.Equal(barber.Profile.Id, withAvatar.Id);
        Assert.Equal(avatarUrl, withAvatar.AvatarUrl);

        using (var delete = await app.Client.DeleteAsync("api/users/me/avatar"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        var afterDelete = await GetSearchPageAsync(anonymous, "Avatar Search City");
        var withoutAvatar = Assert.Single(afterDelete.Items);
        Assert.Equal(barber.Profile.Id, withoutAvatar.Id);
        Assert.Null(withoutAvatar.AvatarUrl);
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_bookings_return_customer_avatar_and_guest_null_but_keep_the_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Avatar Booking Barber");
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(NewEmail("avatar-booking-customer"));
        await app.AuthenticateAsync(customer.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        var now = DateTimeOffset.UtcNow;
        var customerBooking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            now.AddDays(2),
            BookingStatus.Confirmed);
        var guestBooking = await data.CreateGuestBookingAsync(
            barber,
            now.AddDays(2).AddHours(2),
            BookingStatus.Confirmed,
            guestFirstName: "Гость Без Фото",
            guestPhone: "+70000000001");

        await app.AuthenticateAsync(barber.User.Email);

        using var response = await app.Client.GetAsync("api/barber/bookings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);
        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);

        var registered = Assert.Single(
            page.Items,
            item => item.Id == customerBooking.Id);
        Assert.Equal(avatarUrl, registered.Customer.AvatarUrl);

        var guest = Assert.Single(
            page.Items,
            item => item.Id == guestBooking.Id);
        Assert.Null(guest.Customer.AvatarUrl);
        Assert.Equal("+70000000001", guest.Customer.PhoneNumber);
        Assert.Equal("Гость Без Фото", guest.Customer.DisplayName);
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_booking_list_and_detail_return_both_avatars_and_guest_null()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Avatar Admin Barber");

        await app.AuthenticateAsync(barber.User.Email);
        var barberAvatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        var customer = await app.CreateUserAsync(NewEmail("avatar-admin-customer"));
        await app.AuthenticateAsync(customer.Email);
        var customerAvatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        using (var customerProfile = await app.Client.PutAsJsonAsync(
                   $"api/users/{customer.Id}/profile",
                   new
                   {
                       firstName = "Avatar",
                       lastName = "Customer",
                       phoneNumber = "+79990000001",
                       avatarUrl = customerAvatarUrl
                   }))
        {
            Assert.Equal(HttpStatusCode.NoContent, customerProfile.StatusCode);
        }

        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var registered = await data.CreateBookingAsync(
            barber,
            customer.Id,
            start,
            BookingStatus.Confirmed);
        var guest = await data.CreateGuestBookingAsync(
            barber,
            start.AddHours(1),
            BookingStatus.Confirmed,
            guestFirstName: "Гость Админки",
            guestPhone: "+70000000002");

        var admin = await app.CreateUserAsync(NewEmail("avatar-admin"), UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var listResponse = await app.Client.GetAsync("api/admin/bookings");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResponse<AdminBookingResponse>>(JsonOptions);
        Assert.NotNull(page);

        var listRegistered = Assert.Single(page.Items, item => item.Id == registered.Id);
        Assert.Equal(customerAvatarUrl, listRegistered.Customer.AvatarUrl);
        Assert.Equal(barberAvatarUrl, listRegistered.Barber.AvatarUrl);
        Assert.Equal(customer.Email, listRegistered.Customer.Email);
        Assert.Equal("+79990000001", listRegistered.Customer.PhoneNumber);

        var listGuest = Assert.Single(page.Items, item => item.Id == guest.Id);
        Assert.Null(listGuest.Customer.AvatarUrl);
        Assert.Null(listGuest.Customer.Id);
        Assert.Null(listGuest.Customer.Email);
        Assert.Equal("+70000000002", listGuest.Customer.PhoneNumber);
        Assert.Equal(barberAvatarUrl, listGuest.Barber.AvatarUrl);

        // Деталь собирается тем же BuildAdminBookingsQuery/ToAdminResponse: значения совпадают
        // со списком построчно, а не только «оба не null».
        var detailRegistered = await GetAdminBookingAsync(app, registered.Id);
        Assert.Equal(listRegistered.Customer.AvatarUrl, detailRegistered.Customer.AvatarUrl);
        Assert.Equal(listRegistered.Barber.AvatarUrl, detailRegistered.Barber.AvatarUrl);
        Assert.Equal(listRegistered.Customer.Email, detailRegistered.Customer.Email);
        Assert.Equal(listRegistered.Customer.PhoneNumber, detailRegistered.Customer.PhoneNumber);
        Assert.Equal("+79990000001", detailRegistered.Customer.PhoneNumber);

        var detailGuest = await GetAdminBookingAsync(app, guest.Id);
        Assert.Null(detailGuest.Customer.AvatarUrl);
        Assert.Null(detailGuest.Customer.Id);
        Assert.Null(detailGuest.Customer.Email);
        Assert.Equal(listGuest.Customer.AvatarUrl, detailGuest.Customer.AvatarUrl);
        Assert.Equal(listGuest.Barber.AvatarUrl, detailGuest.Barber.AvatarUrl);
        Assert.Equal(listGuest.Customer.PhoneNumber, detailGuest.Customer.PhoneNumber);
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_profile_save_and_profile_update_still_accept_the_avatar()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Regression Avatar Barber");

        await app.AuthenticateAsync(barber.User.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        // Сохранение карточки барбера не имеет аватара в теле и не должно его потерять.
        using (var profileUpdate = await app.Client.PutAsJsonAsync(
                   $"api/barber-profiles/{barber.Profile.Id}",
                   new BarberProfileUpdateRequest(
                       DisplayName: "Regression Avatar Barber",
                       YearsOfExperience: 5,
                       Bio: "Карточка после загрузки аватара",
                       Languages: [],
                       CertificatesAndTraining: [],
                       Achievements: [],
                       PaymentMethods: [],
                       CommunicationFormat: [],
                       Goodies: [],
                       LatePolicy: string.Empty,
                       AdditionalServices: []),
                   TestApp.JsonOptions))
        {
            Assert.Equal(HttpStatusCode.NoContent, profileUpdate.StatusCode);
        }

        Assert.Equal(avatarUrl, (await GetMyProfileAsync(app.Client)).AvatarUrl);

        // Главная регрессия #85: форма отправляет текущий avatarUrl обратно.
        using (var userUpdate = await app.Client.PutAsJsonAsync(
                   $"api/users/{barber.User.Id}/profile",
                   new
                   {
                       firstName = "Regression",
                       lastName = "Barber",
                       phoneNumber = (string?)null,
                       avatarUrl
                   }))
        {
            Assert.Equal(HttpStatusCode.NoContent, userUpdate.StatusCode);
        }

        var profile = await GetMyProfileAsync(app.Client);
        Assert.Equal("Barber", profile.LastName);
        Assert.Equal(avatarUrl, profile.AvatarUrl);
    }

    [Fact(Timeout = 180_000)]
    public async Task Blocked_barber_avatar_is_null_in_search_and_public_card_but_raw_in_admin()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync(
            "Blocked Avatar Barber",
            city: "Blocked Avatar City");

        using var anonymous = app.CreateClient();

        await app.AuthenticateAsync(barber.User.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.BlockAsync(admin, barber.User.Id);

        // Поиск: барбер остаётся в выдаче (счётчики и keyset не ломаются), но фото скрыто.
        var maskedPage = await GetSearchPageAsync(anonymous, "Blocked Avatar City");
        var maskedItem = Assert.Single(maskedPage.Items);

        Assert.Equal(barber.Profile.Id, maskedItem.Id);
        Assert.Null(maskedItem.AvatarUrl);

        var maskedCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.Null(maskedCard.AvatarUrl);

        // Админские поверхности показывают сырой URL и флаг блокировки.
        var userRow = await GetAdminUserRowAsync(admin.Client, barber.User.Email);
        Assert.Equal(avatarUrl, userRow.AvatarUrl);
        Assert.True(userRow.IsAvatarBlocked);

        var barberRow = await GetAdminBarberRowAsync(admin.Client, barber.Profile.Id);
        Assert.Equal(avatarUrl, barberRow.AvatarUrl);
        Assert.True(barberRow.IsAvatarBlocked);

        using (var userDetailResponse = await admin.Client.GetAsync(
                   $"api/admin/users/{barber.User.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, userDetailResponse.StatusCode);

            var userDetail = await userDetailResponse.Content
                .ReadFromJsonAsync<AdminUserDetailResponse>(JsonOptions);

            Assert.NotNull(userDetail);
            Assert.Equal(avatarUrl, userDetail.AvatarUrl);
            Assert.True(userDetail.IsAvatarBlocked);
        }

        using (var barberDetailResponse = await admin.Client.GetAsync(
                   $"api/admin/barbers/{barber.Profile.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, barberDetailResponse.StatusCode);

            var barberDetail = await barberDetailResponse.Content
                .ReadFromJsonAsync<AdminBarberDetailResponse>(JsonOptions);

            Assert.NotNull(barberDetail);
            Assert.Equal(avatarUrl, barberDetail.AvatarUrl);
            Assert.True(barberDetail.IsAvatarBlocked);
        }

        // Возврат отдаёт тот же кадр и в поиске, и в публичной карточке.
        await AvatarModerationApi.UnblockAsync(admin, barber.User.Id);

        var restoredPage = await GetSearchPageAsync(anonymous, "Blocked Avatar City");
        Assert.Equal(avatarUrl, Assert.Single(restoredPage.Items).AvatarUrl);

        var restoredCard = await GetPublicCardAsync(anonymous, barber.Profile.Id);
        Assert.Equal(avatarUrl, restoredCard.AvatarUrl);
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_bookings_mask_blocked_customer_avatar_but_admin_bookings_stay_raw()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Masked Customer Barber");
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(NewEmail("masked-booking-customer"));

        await app.AuthenticateAsync(customer.Email);
        var avatarUrl = await UploadAvatarAsync(app.Client, PngBytes());

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.BlockAsync(admin, customer.Id);

        await app.AuthenticateAsync(barber.User.Email);

        using (var response = await app.Client.GetAsync("api/barber/bookings"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var page = await response.Content
                .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);

            Assert.NotNull(page);

            // Строка записи остаётся: маскируется только аватар клиента.
            var row = Assert.Single(page.Items, item => item.Id == booking.Id);

            Assert.Null(row.Customer.AvatarUrl);
        }

        // Админская поверхность — исключение: там аватар сырой, и для списка, и для детали.
        using (var adminListResponse = await admin.Client.GetAsync("api/admin/bookings"))
        {
            Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);

            var page = await adminListResponse.Content
                .ReadFromJsonAsync<PagedResponse<AdminBookingResponse>>(JsonOptions);

            Assert.NotNull(page);

            var row = Assert.Single(page.Items, item => item.Id == booking.Id);

            Assert.Equal(avatarUrl, row.Customer.AvatarUrl);
        }

        using (var adminDetailResponse = await admin.Client.GetAsync(
                   $"api/admin/bookings/{booking.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, adminDetailResponse.StatusCode);

            var detail = await adminDetailResponse.Content
                .ReadFromJsonAsync<AdminBookingResponse>(JsonOptions);

            Assert.NotNull(detail);
            Assert.Equal(avatarUrl, detail.Customer.AvatarUrl);
        }
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

    private static async Task<AdminUserResponse> GetAdminUserRowAsync(
        HttpClient client,
        string email)
    {
        using var response = await client.GetAsync(
            $"api/admin/users?search={Uri.EscapeDataString(email)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminUserResponse>>(JsonOptions);

        Assert.NotNull(page);

        return Assert.Single(page.Items);
    }

    private static async Task<AdminBarberResponse> GetAdminBarberRowAsync(
        HttpClient client,
        Guid profileId)
    {
        using var response = await client.GetAsync("api/admin/barbers?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminBarberResponse>>(JsonOptions);

        Assert.NotNull(page);

        return Assert.Single(page.Items, item => item.Id == profileId);
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

    private static async Task<AdminBookingResponse> GetAdminBookingAsync(TestApp app, Guid id)
    {
        using var response = await app.Client.GetAsync($"api/admin/bookings/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await response.Content.ReadFromJsonAsync<AdminBookingResponse>(JsonOptions);
        Assert.NotNull(row);

        return row;
    }

    private static async Task<MyProfileResponse> GetMyProfileAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<MyProfileResponse>(JsonOptions);
        Assert.NotNull(profile);

        return profile;
    }

    private static async Task<string> UploadAvatarAsync(HttpClient client, byte[] content)
    {
        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "avatar.png");

        using var response = await client.PostAsync("api/users/me/avatar", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AvatarUploadResponse>();
        Assert.NotNull(body);

        return body.AvatarUrl;
    }

    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        Require(user.LinkBarberProfile(barber.Profile.Id));
        await db.SaveChangesAsync();
    }

    private static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@barbersearch.test";

    /// <summary>
    /// Минимальный PNG с настоящими сигнатурой и <c>IHDR</c>: инспектор читает размеры
    /// из заголовка и не декодирует файл.
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
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => error.Code))}");
        }
    }
}
