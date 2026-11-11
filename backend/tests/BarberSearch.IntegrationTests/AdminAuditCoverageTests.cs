using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSearch.Contracts.Bookings.AddBookingBarberNote;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.UpdateSalonContactInfo;
using BarberSearch.Contracts.Specializations.RenameSpecialization;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Покрытие журнала аудита (issue-41): правки салонов, каталога и броней пишутся строками
/// <c>{entity}.{created|updated|deleted}</c>, создание брони сознательно не пишется,
/// а фильтр <c>GET api/admin/audit?entityType=</c> принимает четыре новых имени.
/// </summary>
/// <remarks>
/// <para>
/// Один <c>[Fact]</c> — одно обещание: позитив («строка появилась с нужными полями»)
/// и негатив («строки нет») — разные тесты. Строки журнала появляются от реальных
/// действий через API; сущности готовятся доменными хелперами <see cref="TestData"/>.
/// SQL-вставок в проверках нет — читается то, что записал <c>AuditTrail</c>.
/// </para>
/// <para>
/// Строки <c>user.created</c> от регистрации клиента появиться не могут: тестовые
/// пользователи создаются через <see cref="TestApp.CreateUserAsync"/>, то есть напрямую
/// через контекст, а не через репозиторий с аудитом.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AdminAuditCoverageTests
{
    private const string ENDPOINT = "api/admin/audit";

    private readonly PostgresFixture _database;

    public AdminAuditCoverageTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Salon_creation_writes_created_row_with_values_and_admin_actor()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await CreateAdminAsync(app);

        var salonId = await CreateSalonAsync(app, "Values Salon");

        var row = Assert.Single(
            await new TestData(app).ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "salon" && candidate.EntityId == salonId);

        Assert.Equal("salon.created", row.Action);
        Assert.Equal("salon", row.EntityType);
        Assert.Equal(admin.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var values = payload.RootElement.GetProperty("values");

        Assert.Equal("Values Salon", values.GetProperty("name").GetString());
        Assert.Equal("Berlin", values.GetProperty("city").GetString());
        Assert.True(values.GetProperty("isActive").GetBoolean());
        Assert.Equal(52.52, values.GetProperty("latitude").GetDouble());
        Assert.Equal(13.405, values.GetProperty("longitude").GetDouble());
    }

    [Fact(Timeout = 180_000)]
    public async Task Salon_contact_change_writes_only_the_phone_change()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var salon = await data.CreateSalonAsync("Contact Salon", phoneNumber: "+4930000000");
        await CreateAdminAsync(app);

        using (var response = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salon.Id}/contact-info",
                   new UpdateSalonContactInfoRequest("+4930999999", null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "salon" && candidate.EntityId == salon.Id);

        Assert.Equal("salon.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");
        var phone = changes.GetProperty("phoneNumber");

        Assert.Equal("+4930000000", phone.GetProperty("from").GetString());
        Assert.Equal("+4930999999", phone.GetProperty("to").GetString());

        // Неизменённые поля в diff не попадают: payload — только то, что действительно изменилось.
        Assert.Single(changes.EnumerateObject());
        Assert.False(changes.TryGetProperty("websiteUrl", out _));
        Assert.False(changes.TryGetProperty("address", out _));
        Assert.False(changes.TryGetProperty("name", out _));
    }

    [Fact(Timeout = 180_000)]
    public async Task Hair_service_deactivation_writes_is_active_change()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var hairService = await data.CreateHairServiceAsync("Deactivation Service");
        await CreateAdminAsync(app);

        using (var response = await app.Client.DeleteAsync(
                   $"api/hair-services/{hairService.Id}/activation"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "hair_service"
                && candidate.EntityId == hairService.Id);

        Assert.Equal("hair_service.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");
        var isActive = changes.GetProperty("isActive");

        Assert.True(isActive.GetProperty("from").GetBoolean());
        Assert.False(isActive.GetProperty("to").GetBoolean());
        Assert.Single(changes.EnumerateObject());
    }

    [Fact(Timeout = 180_000)]
    public async Task Specialization_rename_writes_name_change()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var specialization = await data.CreateSpecializationAsync("Coverage Fade");
        await CreateAdminAsync(app);

        using (var response = await app.Client.PutAsJsonAsync(
                   $"api/specializations/{specialization.Id}",
                   new RenameSpecializationRequest(
                       "Coverage Fade 2",
                       $"About {specialization.Name}")))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "specialization"
                && candidate.EntityId == specialization.Id);

        Assert.Equal("specialization.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");
        var name = changes.GetProperty("name");

        Assert.Equal("Coverage Fade", name.GetProperty("from").GetString());
        Assert.Equal("Coverage Fade 2", name.GetProperty("to").GetString());

        // Описание не менялось — в diff его нет.
        Assert.Single(changes.EnumerateObject());
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_cancellation_writes_booking_status_change()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Cancellation Barber");
        var customer = await app.CreateUserAsync(
            $"audit-coverage-customer-{Guid.NewGuid():N}@barbersearch.test");
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero));
        var admin = await CreateAdminAsync(app);

        using (var response = await app.Client.PostAsync(
                   $"api/bookings/{booking.Id}/cancellation",
                   null))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "booking"
                && candidate.EntityId == booking.Id);

        Assert.Equal("booking.updated", row.Action);
        Assert.Equal(admin.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var status = payload.RootElement.GetProperty("changes").GetProperty("status");

        Assert.Equal("Pending", status.GetProperty("from").GetString());
        Assert.Equal("Cancelled", status.GetProperty("to").GetString());
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_note_writes_barber_note_change_with_barber_actor()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Note Barber");
        var customer = await app.CreateUserAsync(
            $"audit-coverage-note-customer-{Guid.NewGuid():N}@barbersearch.test");
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero));

        await app.AuthenticateAsync(barber.User.Email);

        using (var response = await app.Client.PutAsJsonAsync(
                   $"api/bookings/{booking.Id}/barber-note",
                   new AddBookingBarberNoteRequest("Bring clippers")))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "booking"
                && candidate.EntityId == booking.Id);

        Assert.Equal("booking.updated", row.Action);
        Assert.Equal(barber.User.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var barberNote = payload.RootElement.GetProperty("changes").GetProperty("barberNote");

        Assert.Equal(JsonValueKind.Null, barberNote.GetProperty("from").ValueKind);
        Assert.Equal("Bring clippers", barberNote.GetProperty("to").GetString());
    }

    [Fact(Timeout = 180_000)]
    public async Task Client_booking_creation_writes_no_booking_created_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Create Barber");
        var customer = await app.CreateUserAsync(
            $"audit-coverage-create-customer-{Guid.NewGuid():N}@barbersearch.test");

        await app.AuthenticateAsync(customer.Email);

        var before = await data.CountAuditRowsAsync("booking");

        // Окно бронирования считается от «сейчас»: тест создаёт бронь через API, поэтому
        // начало обязано быть в будущем, а не фиксированной датой среза.
        var startAt = DateTimeOffset.UtcNow.AddDays(10);

        Guid bookingId;

        using (var response = await app.Client.PostAsJsonAsync(
                   "api/bookings",
                   new CreateBookingRequest(
                       barber.Profile.Id,
                       barber.Service.Id,
                       barber.Salon.Id,
                       startAt,
                       startAt.AddMinutes(30),
                       $"audit-create-{Guid.NewGuid():N}",
                       null)))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
            Assert.NotNull(body);
            bookingId = body.Id;
        }

        // Создание брони — решение, а не забывчивость: строка bookings уже хранит владельца,
        // время и цену, а строка аудита на каждую бронь раздувала бы таблицу без новой информации.
        Assert.NotEqual(Guid.Empty, bookingId);
        Assert.Equal(before, await data.CountAuditRowsAsync("booking"));
        Assert.DoesNotContain(
            await data.ReadAuditRowsAsync(),
            row => row.Action == "booking.created");
    }

    [Fact(Timeout = 180_000)]
    public async Task Entity_type_filter_returns_only_the_requested_new_type()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Filter Barber");
        var customer = await app.CreateUserAsync(
            $"audit-coverage-filter-customer-{Guid.NewGuid():N}@barbersearch.test");
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        var hairService = await data.CreateHairServiceAsync("Filter Service");
        var specialization = await data.CreateSpecializationAsync("Filter Fade");
        await CreateAdminAsync(app);

        var salonId = await CreateSalonAsync(app, "Filter Salon");

        using (var response = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/contact-info",
                   new UpdateSalonContactInfoRequest("+4930222222", null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await app.Client.DeleteAsync(
                   $"api/hair-services/{hairService.Id}/activation"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await app.Client.PutAsJsonAsync(
                   $"api/specializations/{specialization.Id}",
                   new RenameSpecializationRequest(
                       "Filter Fade 2",
                       $"About {specialization.Name}")))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await app.Client.PostAsync(
                   $"api/bookings/{booking.Id}/cancellation",
                   null))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // У салона две строки: создание и правка контактов. Свежие сверху.
        using (var response = await app.Client.GetAsync($"{ENDPOINT}?entityType=salon"))
        {
            var items = await ReadPageAsync(response);

            Assert.Equal(2, items.Count);
            Assert.All(items, item => Assert.Equal("salon", item.EntityType));
            Assert.Equal("salon.updated", items[0].Action);
            Assert.Equal("salon.created", items[1].Action);
        }

        var expected = new[]
        {
            (Name: "hair_service", Action: "hair_service.updated", EntityId: hairService.Id),
            (Name: "specialization", Action: "specialization.updated", EntityId: specialization.Id),
            (Name: "booking", Action: "booking.updated", EntityId: booking.Id)
        };

        foreach (var (name, action, entityId) in expected)
        {
            using var response = await app.Client.GetAsync($"{ENDPOINT}?entityType={name}");
            var item = Assert.Single(await ReadPageAsync(response));

            Assert.Equal(name, item.EntityType);
            Assert.Equal(action, item.Action);
            Assert.Equal(entityId, item.EntityId);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Entity_type_filter_is_trimmed_and_case_insensitive()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var hairService = await data.CreateHairServiceAsync("Case Service");
        await CreateAdminAsync(app);

        var salonId = await CreateSalonAsync(app, "Case Salon");

        using (var response = await app.Client.DeleteAsync(
                   $"api/hair-services/{hairService.Id}/activation"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await app.Client.GetAsync($"{ENDPOINT}?entityType=SALON"))
        {
            var item = Assert.Single(await ReadPageAsync(response));
            Assert.Equal("salon", item.EntityType);
            Assert.Equal(salonId, item.EntityId);
        }

        using (var response = await app.Client.GetAsync(
                   $"{ENDPOINT}?entityType={Uri.EscapeDataString(" Hair_Service ")}"))
        {
            var item = Assert.Single(await ReadPageAsync(response));
            Assert.Equal("hair_service", item.EntityType);
            Assert.Equal(hairService.Id, item.EntityId);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Unknown_entity_type_values_are_rejected_with_400()
    {
        await using var app = await TestApp.StartAsync(_database);
        await CreateAdminAsync(app);

        // Числа по-прежнему отвергаются: разбор строковый, Enum.TryParse не используется.
        foreach (var value in new[] { "services", "bookings", "0", "8" })
        {
            using var response = await app.Client.GetAsync($"{ENDPOINT}?entityType={value}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "admin.audit.entityType",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Booking_payload_has_no_idempotency_key_customer_note_or_dates()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Payload Barber");
        var customer = await app.CreateUserAsync(
            $"audit-coverage-payload-customer-{Guid.NewGuid():N}@barbersearch.test");

        await app.AuthenticateAsync(customer.Email);

        var idempotencyKey = $"audit-payload-key-{Guid.NewGuid():N}";
        var customerNote = "customer-note-must-not-appear";

        var startAt = DateTimeOffset.UtcNow.AddDays(10);

        Guid bookingId;

        using (var response = await app.Client.PostAsJsonAsync(
                   "api/bookings",
                   new CreateBookingRequest(
                       barber.Profile.Id,
                       barber.Service.Id,
                       barber.Salon.Id,
                       startAt,
                       startAt.AddMinutes(30),
                       idempotencyKey,
                       customerNote)))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
            Assert.NotNull(body);
            bookingId = body.Id;
        }

        await CreateAdminAsync(app);

        using (var response = await app.Client.PostAsync(
                   $"api/bookings/{bookingId}/cancellation",
                   null))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "booking"
                && candidate.EntityId == bookingId);

        Assert.Equal("booking.updated", row.Action);

        // Ключ идемпотентности — технический ключ клиента, а не история изменения; заметка
        // клиента и служебные даты тоже не входят в allowlist брони.
        Assert.DoesNotContain(idempotencyKey, row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(customerNote, row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("idempotencyKey", row.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customerNote", row.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("startAt", row.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endAt", row.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", row.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 180_000)]
    public async Task Journal_access_is_unchanged_for_anonymous_customer_barber_and_admin()
    {
        await using var app = await TestApp.StartAsync(_database);

        using (var anonymous = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var customer = await app.CreateUserAsync(
            $"audit-coverage-access-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using (var forbidden = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var barber = await app.CreateUserAsync(
            $"audit-coverage-access-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        using (var forbiddenBarber = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenBarber.StatusCode);
        }

        await CreateAdminAsync(app);

        using var allowed = await app.Client.GetAsync(ENDPOINT);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Avatar_block_and_unblock_write_one_user_updated_row_each()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var user = await app.CreateUserAsync(
            $"audit-coverage-avatar-{Guid.NewGuid():N}@barbersearch.test");

        await app.AuthenticateAsync(user.Email);
        await UploadAvatarAsync(app.Client);

        // Загрузка аватара — техническая правка: AvatarUrl в allowlist не входит,
        // поэтому строки user.updated от неё нет.
        Assert.DoesNotContain(
            await data.ReadAuditRowsAsync(),
            row => row.EntityType == "user" && row.EntityId == user.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        // Решение администратора по кадру в allowlist не входит: строка аудита не появляется.
        await AvatarModerationApi.ApproveAsync(admin, user.Id);

        Assert.DoesNotContain(
            await data.ReadAuditRowsAsync(),
            row => row.EntityType == "user" && row.EntityId == user.Id);

        await AvatarModerationApi.BlockAsync(admin, user.Id);

        var blockRow = Assert.Single(
            await data.ReadAuditRowsAsync(),
            row => row.EntityType == "user" && row.EntityId == user.Id);

        Assert.Equal("user.updated", blockRow.Action);
        Assert.Equal(admin.Admin.Id, blockRow.ActorUserId);

        using (var payload = JsonDocument.Parse(blockRow.Payload))
        {
            var changes = payload.RootElement.GetProperty("changes");

            // Только флаг: URL в payload не попадает.
            Assert.Single(changes.EnumerateObject());

            var blocked = changes.GetProperty("isAvatarBlocked");

            Assert.False(blocked.GetProperty("from").GetBoolean());
            Assert.True(blocked.GetProperty("to").GetBoolean());
        }

        // Идемпотентная повторная блокировка не меняет UpdatedAt и не пишет аудит.
        await AvatarModerationApi.BlockAsync(admin, user.Id);

        Assert.Single(
            await data.ReadAuditRowsAsync(),
            row => row.EntityType == "user" && row.EntityId == user.Id);

        await AvatarModerationApi.UnblockAsync(admin, user.Id);

        var userRows = (await data.ReadAuditRowsAsync())
            .Where(row => row.EntityType == "user" && row.EntityId == user.Id)
            .ToList();

        Assert.Equal(2, userRows.Count);

        // Свежие сверху: первая строка — возврат фото.
        var unblockRow = userRows[0];

        Assert.Equal("user.updated", unblockRow.Action);
        Assert.Equal(admin.Admin.Id, unblockRow.ActorUserId);

        using (var payload = JsonDocument.Parse(unblockRow.Payload))
        {
            var unblocked = payload.RootElement
                .GetProperty("changes")
                .GetProperty("isAvatarBlocked");

            Assert.True(unblocked.GetProperty("from").GetBoolean());
            Assert.False(unblocked.GetProperty("to").GetBoolean());
        }
    }

    private static async Task<Guid> CreateSalonAsync(TestApp app, string name)
    {
        using var response = await TestApp.PostJsonAsync(
            app.Client,
            "api/salons",
            new CreateSalonRequest(
                name,
                "Teststrasse 1",
                "Berlin",
                "Germany",
                "Europe/Berlin",
                52.5200,
                13.4050,
                null,
                null,
                null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    private static async Task<User> CreateAdminAsync(TestApp app)
    {
        var admin = await app.CreateUserAsync(
            $"audit-coverage-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);

        return admin;
    }

    private static async Task<IReadOnlyList<AuditEntry>> ReadPageAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        return document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => new AuditEntry(
                item.GetProperty("id").GetGuid(),
                item.GetProperty("action").GetString()!,
                item.GetProperty("entityType").GetString()!,
                item.GetProperty("entityId").GetGuid()))
            .ToList();
    }

    private sealed record AuditEntry(
        Guid Id,
        string Action,
        string EntityType,
        Guid EntityId);

    /// <summary>
    /// Загружает аватар текущего пользователя настоящим multipart-запросом: только так
    /// проверяется, что загрузка (в отличие от решения администратора) аудит не пишет.
    /// </summary>
    private static async Task UploadAvatarAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PngBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "avatar.png");

        using var response = await client.PostAsync("api/users/me/avatar", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

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
}
