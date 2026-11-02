using System.Net;
using System.Text.Json;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админское чтение броней (<c>GET api/admin/bookings</c> и <c>GET api/admin/bookings/{id}</c>)
/// на реальной PostgreSQL: доступ, порядок свежими сверху, страницы без потерь, фильтр
/// статуса, поиск по email и точному id, курсор, карточка и действия администратора
/// над чужой бронью.
/// </summary>
/// <remarks>
/// <para>
/// Брони создаются доменными фабриками через <see cref="TestData"/>: у таблицы есть
/// exclusion-ограничение и правила переходов статуса, поэтому SQL-вставка обошла бы модель.
/// </para>
/// <para>
/// DTO чтения объявлены локально: тест проверяет контракт и должен компилироваться до того,
/// как появятся production-контракты. <c>status</c> читается строкой — это и есть обещание
/// JSON-контракта «имя <c>BookingStatus</c>», а не число.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AdminBookingReadTests
{
    private const string ENDPOINT = "api/admin/bookings";

    private const string BOOKINGS_ENDPOINT = "api/bookings";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgresFixture _database;

    public AdminBookingReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_gets_401_and_non_admin_gets_403()
    {
        await using var app = await TestApp.StartAsync(_database);

        using (var anonymousList = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        }

        using (var anonymousDetail = await app.Client.GetAsync(
                   $"{ENDPOINT}/{Guid.CreateVersion7()}"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDetail.StatusCode);
        }

        var customer = await app.CreateUserAsync(
            $"admin-bookings-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using (var list = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        }

        using (var detail = await app.Client.GetAsync($"{ENDPOINT}/{Guid.CreateVersion7()}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        }

        var barber = await app.CreateUserAsync(
            $"admin-bookings-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        using (var list = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        }

        using (var detail = await app.Client.GetAsync($"{ENDPOINT}/{Guid.CreateVersion7()}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        }

        var admin = await app.CreateUserAsync(
            $"admin-bookings-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var list = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        }

        using (var detail = await app.Client.GetAsync($"{ENDPOINT}/{Guid.CreateVersion7()}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_reads_bookings_of_every_customer_newest_first()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("List Barber");

        var first = await app.CreateUserAsync(
            $"admin-bookings-first-{Guid.NewGuid():N}@barbersearch.test");
        var second = await app.CreateUserAsync(
            $"admin-bookings-second-{Guid.NewGuid():N}@barbersearch.test");
        var third = await app.CreateUserAsync(
            $"admin-bookings-third-{Guid.NewGuid():N}@barbersearch.test");

        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var oldest = await data.CreateBookingAsync(barber, first.Id, start);
        var middle = await data.CreateBookingAsync(barber, second.Id, start.AddHours(1));
        var newest = await data.CreateBookingAsync(barber, third.Id, start.AddHours(2));

        var admin = await app.CreateUserAsync(
            $"admin-bookings-list-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var page = await GetPageAsync(app, ENDPOINT);

        Assert.Equal(
            new[] { newest.Id, middle.Id, oldest.Id },
            page.Items.Select(item => item.Id));
        Assert.Equal(3, page.Items.Select(item => item.Customer.Id).Distinct().Count());
    }

    [Fact(Timeout = 180_000)]
    public async Task Newest_bookings_come_first_across_pages_without_duplicates()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var firstBarber = await data.CreateBookableBarberAsync("Page Barber One");
        var secondBarber = await data.CreateBookableBarberAsync("Page Barber Two");
        var customer = await app.CreateUserAsync(
            $"admin-bookings-paging-{Guid.NewGuid():N}@barbersearch.test");

        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var seeded = new List<SeededBooking>();

        // 15 визитов первого барбера, затем 10 визитов второго в те же минуты:
        // start_at не уникален, и без второго ключа (id) страницы теряли бы строки.
        for (var index = 0; index < 15; index++)
        {
            seeded.Add(await data.CreateBookingAsync(
                firstBarber,
                customer.Id,
                start.AddMinutes(index * 30)));
        }

        for (var index = 0; index < 10; index++)
        {
            seeded.Add(await data.CreateBookingAsync(
                secondBarber,
                customer.Id,
                start.AddMinutes(index * 30)));
        }

        var expected = seeded
            .OrderByDescending(booking => booking.StartAt)
            .ThenBy(booking => booking.Id)
            .Select(booking => booking.Id)
            .ToList();

        var admin = await app.CreateUserAsync(
            $"admin-bookings-paging-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var collected = new List<Guid>();
        string? cursor = null;

        for (var pageIndex = 0; pageIndex < 10; pageIndex++)
        {
            var page = await GetPageAsync(app, BuildUrl(cursor: cursor, pageSize: 10));

            Assert.InRange(page.Items.Count, 1, 10);
            collected.AddRange(page.Items.Select(item => item.Id));

            cursor = page.NextCursor;

            if (cursor is null)
            {
                break;
            }
        }

        // 25 строк по 10 на страницу: 10 + 10 + 5, третья страница закрывает список.
        Assert.Equal(3, (collected.Count + 9) / 10);
        Assert.Equal(expected, collected);
        Assert.Equal(expected.Count, collected.Distinct().Count());
    }

    [Fact(Timeout = 180_000)]
    public async Task Status_filter_returns_only_that_status()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Status Barber");
        var customer = await app.CreateUserAsync(
            $"admin-bookings-status-{Guid.NewGuid():N}@barbersearch.test");

        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var pending = await data.CreateBookingAsync(
            barber, customer.Id, start, BookingStatus.Pending);
        var confirmed = await data.CreateBookingAsync(
            barber, customer.Id, start.AddHours(1), BookingStatus.Confirmed);
        var completed = await data.CreateBookingAsync(
            barber, customer.Id, start.AddHours(2), BookingStatus.Completed);
        var cancelled = await data.CreateBookingAsync(
            barber, customer.Id, start.AddHours(3), BookingStatus.Cancelled);
        var noShow = await data.CreateBookingAsync(
            barber, customer.Id, start.AddHours(4), BookingStatus.NoShow);

        var admin = await app.CreateUserAsync(
            $"admin-bookings-status-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var pendingPage = await GetPageAsync(app, BuildUrl(status: "Pending"));
        Assert.Equal(new[] { pending.Id }, pendingPage.Items.Select(item => item.Id));
        Assert.DoesNotContain(pendingPage.Items, item => item.Id == cancelled.Id);

        var allPage = await GetPageAsync(app, BuildUrl(status: "all"));
        Assert.Equal(5, allPage.Items.Count);

        foreach (var expected in new[]
                 {
                     (Name: "confirmed", Id: confirmed.Id),
                     (Name: "CONFIRMED", Id: confirmed.Id),
                     (Name: "NoShow", Id: noShow.Id)
                 })
        {
            var page = await GetPageAsync(app, BuildUrl(status: expected.Name));

            Assert.Equal(new[] { expected.Id }, page.Items.Select(item => item.Id));
            Assert.DoesNotContain(page.Items, item => item.Id == pending.Id);
            Assert.DoesNotContain(page.Items, item => item.Id == completed.Id);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Unknown_status_is_rejected_with_400()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"admin-bookings-status-bad-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        foreach (var value in new[] { "wrong", "upcoming", "0" })
        {
            await AssertBadRequestAsync(app, BuildUrl(status: value), "admin.bookings.status");
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Search_matches_customer_email_case_insensitively()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Search Barber");

        var customer = await app.CreateUserAsync(
            $"e2e-bookings-{Guid.NewGuid():N}@barbersearch.test");
        var other = await app.CreateUserAsync(
            $"plain-bookings-{Guid.NewGuid():N}@barbersearch.test");

        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var older = await data.CreateBookingAsync(barber, customer.Id, start);
        var newer = await data.CreateBookingAsync(barber, customer.Id, start.AddHours(1));
        var foreign = await data.CreateBookingAsync(barber, other.Id, start.AddHours(2));

        var admin = await app.CreateUserAsync(
            $"admin-bookings-search-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        foreach (var search in new[] { "e2e-", "E2E-" })
        {
            var page = await GetPageAsync(app, BuildUrl(search: search));

            Assert.Equal(
                new[] { newer.Id, older.Id },
                page.Items.Select(item => item.Id));
            Assert.DoesNotContain(page.Items, item => item.Id == foreign.Id);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Search_escapes_wildcards()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Wildcard Barber");
        var customer = await app.CreateUserAsync(
            $"admin-bookings-wildcard-{Guid.NewGuid():N}@barbersearch.test");

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero));

        var admin = await app.CreateUserAsync(
            $"admin-bookings-wildcard-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var unfiltered = await GetPageAsync(app, ENDPOINT);
        Assert.Contains(unfiltered.Items, item => item.Id == booking.Id);

        // «%» и «_» — литералы, а не шаблон ILIKE: в адресе их нет, значит совпадений нет.
        foreach (var search in new[] { "%", "_" })
        {
            var page = await GetPageAsync(app, BuildUrl(search: search));
            Assert.Empty(page.Items);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Search_by_booking_id_returns_exactly_that_booking()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Id Search Barber");
        var customer = await app.CreateUserAsync(
            $"admin-bookings-id-search-{Guid.NewGuid():N}@barbersearch.test");

        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var expected = await data.CreateBookingAsync(barber, customer.Id, start);
        var other = await data.CreateBookingAsync(barber, customer.Id, start.AddHours(1));

        var admin = await app.CreateUserAsync(
            $"admin-bookings-id-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var page = await GetPageAsync(app, BuildUrl(search: expected.Id.ToString("D")));

        var row = Assert.Single(page.Items);
        Assert.Equal(expected.Id, row.Id);
        Assert.DoesNotContain(page.Items, item => item.Id == other.Id);
    }

    [Fact(Timeout = 120_000)]
    public async Task Search_by_unknown_booking_id_returns_empty()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"admin-bookings-unknown-id-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var page = await GetPageAsync(app, BuildUrl(search: Guid.CreateVersion7().ToString("D")));

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact(Timeout = 120_000)]
    public async Task Search_longer_than_100_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"admin-bookings-search-limit-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var boundary = await app.Client.GetAsync(BuildUrl(search: new string('a', 100))))
        {
            Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        }

        await AssertBadRequestAsync(
            app,
            BuildUrl(search: new string('a', 101)),
            "admin.bookings.search");
    }

    [Fact(Timeout = 180_000)]
    public async Task Detail_returns_booking_with_context()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Detail Barber", city: "Hamburg");
        var customer = await app.CreateUserAsync(
            $"admin-bookings-detail-{Guid.NewGuid():N}@barbersearch.test");
        await SetCustomerProfileAsync(app, customer.Id, "Иван", "Петров", "+7 999 123-45-67");

        var startAt = new DateTimeOffset(2026, 9, 12, 18, 30, 0, TimeSpan.Zero);
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            price: 55.50m,
            customerNote: "Позвоните за час",
            barberNote: "Клиент предупреждён");

        var admin = await app.CreateUserAsync(
            $"admin-bookings-detail-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync($"{ENDPOINT}/{booking.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await ReadBookingAsync(response);

        Assert.Equal(booking.Id, row.Id);
        Assert.Equal("Pending", row.Status);
        Assert.Equal(startAt, row.StartAt);
        Assert.Equal(startAt.AddMinutes(30), row.EndAt);
        Assert.Equal(55.50m, row.Price);
        Assert.Equal("Позвоните за час", row.CustomerNote);
        Assert.Equal("Клиент предупреждён", row.BarberNote);
        Assert.NotEqual(default, row.CreatedAt);

        Assert.Equal(customer.Id, row.Customer.Id);
        Assert.Equal("Иван", row.Customer.FirstName);
        Assert.Equal("Петров", row.Customer.LastName);
        Assert.Equal(customer.Email, row.Customer.Email);
        Assert.Equal("+7 999 123-45-67", row.Customer.PhoneNumber);

        Assert.Equal(barber.Profile.Id, row.Barber.ProfileId);
        Assert.Equal("Detail Barber", row.Barber.DisplayName);

        Assert.Equal("Haircut Detail Barber", row.Service.Name);

        Assert.Equal(barber.Salon.Id, row.Salon.Id);
        Assert.Equal("Salon Detail Barber", row.Salon.Name);
        Assert.Equal("Teststrasse 1", row.Salon.Address);
        Assert.Equal("Hamburg", row.Salon.City);
        Assert.Equal("Europe/Berlin", row.Salon.TimeZoneId);
    }

    [Fact(Timeout = 120_000)]
    public async Task Missing_booking_detail_is_404()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"admin-bookings-missing-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync($"{ENDPOINT}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(
            "admin.bookings.notFound",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task Page_size_out_of_range_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"admin-bookings-page-size-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        foreach (var pageSize in new[] { 0, 101 })
        {
            await AssertBadRequestAsync(
                app,
                BuildUrl(pageSize: pageSize),
                "admin.bookings.pageSize");
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Broken_cursor_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"admin-bookings-cursor-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        // Две строки журнала дают валидный курсор чужого списка: та же форма (4 части),
        // но своя область в курсоре, поэтому он обязан быть отвергнут, а не листать чужой порядок.
        var occurredAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        await data.SeedAuditAsync(
            occurredAt, admin.Id, "user.updated", "user", Guid.NewGuid(), """{"changes":{}}""");
        await data.SeedAuditAsync(
            occurredAt.AddMinutes(1), admin.Id, "user.updated", "user", Guid.NewGuid(), """{"changes":{}}""");

        await app.AuthenticateAsync(admin.Email);

        string? foreignCursor;

        using (var audit = await app.Client.GetAsync("api/admin/audit?pageSize=1"))
        {
            Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

            using var document = JsonDocument.Parse(await audit.Content.ReadAsStringAsync());
            foreignCursor = document.RootElement.GetProperty("nextCursor").GetString();
        }

        Assert.False(string.IsNullOrWhiteSpace(foreignCursor));

        await AssertBadRequestAsync(
            app,
            BuildUrl(cursor: foreignCursor),
            "admin.bookings.cursor.invalid");
        await AssertBadRequestAsync(
            app,
            BuildUrl(cursor: "garbage"),
            "admin.bookings.cursor.invalid");
    }

    [Fact(Timeout = 180_000)]
    public async Task Row_carries_salon_time_zone_and_notes()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Notes Barber");
        var customer = await app.CreateUserAsync(
            $"admin-bookings-notes-{Guid.NewGuid():N}@barbersearch.test");

        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var withNotes = await data.CreateBookingAsync(
            barber,
            customer.Id,
            start,
            customerNote: "Позвоните за час",
            barberNote: "Клиент предупреждён");
        var withoutNotes = await data.CreateBookingAsync(barber, customer.Id, start.AddHours(1));

        var admin = await app.CreateUserAsync(
            $"admin-bookings-notes-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var page = await GetPageAsync(app, BuildUrl(search: customer.Email));

        var withNotesRow = Assert.Single(page.Items, item => item.Id == withNotes.Id);
        Assert.Equal("Позвоните за час", withNotesRow.CustomerNote);
        Assert.Equal("Клиент предупреждён", withNotesRow.BarberNote);
        Assert.Equal("Europe/Berlin", withNotesRow.Salon.TimeZoneId);

        var withoutNotesRow = Assert.Single(page.Items, item => item.Id == withoutNotes.Id);
        Assert.Null(withoutNotesRow.CustomerNote);
        Assert.Null(withoutNotesRow.BarberNote);
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_can_cancel_and_mark_no_show_and_the_list_reflects_it()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Actions Barber");
        var customer = await app.CreateUserAsync(
            $"admin-bookings-actions-{Guid.NewGuid():N}@barbersearch.test");

        var now = DateTimeOffset.UtcNow;
        var pending = await data.CreateBookingAsync(
            barber, customer.Id, now.AddDays(2), BookingStatus.Pending);
        var confirmedInThePast = await data.CreateBookingAsync(
            barber, customer.Id, now.AddDays(-2), BookingStatus.Confirmed);

        var admin = await app.CreateUserAsync(
            $"admin-bookings-actions-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var cancelled = await app.Client.PostAsync(
                   $"{BOOKINGS_ENDPOINT}/{pending.Id}/cancellation",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        using (var noShow = await app.Client.PostAsync(
                   $"{BOOKINGS_ENDPOINT}/{confirmedInThePast.Id}/no-show",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, noShow.StatusCode);
        }

        using (var pendingDetail = await app.Client.GetAsync($"{ENDPOINT}/{pending.Id}"))
        {
            var row = await ReadBookingAsync(pendingDetail);
            Assert.Equal("Cancelled", row.Status);
        }

        using (var noShowDetail = await app.Client.GetAsync($"{ENDPOINT}/{confirmedInThePast.Id}"))
        {
            var row = await ReadBookingAsync(noShowDetail);
            Assert.Equal("NoShow", row.Status);
        }

        var page = await GetPageAsync(app, BuildUrl(search: customer.Email));

        Assert.Equal(
            "Cancelled",
            Assert.Single(page.Items, item => item.Id == pending.Id).Status);
        Assert.Equal(
            "NoShow",
            Assert.Single(page.Items, item => item.Id == confirmedInThePast.Id).Status);
    }

    private static string BuildUrl(
        string? status = null,
        string? search = null,
        string? cursor = null,
        int? pageSize = null)
    {
        var parameters = new List<string>();

        if (status is not null)
        {
            parameters.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (search is not null)
        {
            parameters.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (cursor is not null)
        {
            parameters.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (pageSize is { } size)
        {
            parameters.Add($"pageSize={size}");
        }

        return parameters.Count == 0
            ? ENDPOINT
            : $"{ENDPOINT}?{string.Join('&', parameters)}";
    }

    private static async Task<BookingPage> GetPageAsync(TestApp app, string url)
    {
        using var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = JsonSerializer.Deserialize<BookingPage>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        Assert.NotNull(page);

        return page;
    }

    private static async Task<AdminBookingRow> ReadBookingAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = JsonSerializer.Deserialize<AdminBookingRow>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        Assert.NotNull(row);

        return row;
    }

    private static async Task AssertBadRequestAsync(TestApp app, string url, string code)
    {
        using var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            code,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private static async Task SetCustomerProfileAsync(
        TestApp app,
        Guid userId,
        string firstName,
        string lastName,
        string phoneNumber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == userId);

        var updated = user.UpdateProfile(firstName, lastName, phoneNumber, avatarUrl: null);
        if (updated.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", updated.Error.Errors.Select(error => error.Code))}");
        }

        await db.SaveChangesAsync();
    }

    private sealed record BookingPage(
        IReadOnlyList<AdminBookingRow> Items,
        string? NextCursor);

    private sealed record AdminBookingRow(
        Guid Id,
        string Status,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        decimal Price,
        string? CustomerNote,
        string? BarberNote,
        DateTimeOffset CreatedAt,
        AdminBookingCustomer Customer,
        AdminBookingBarber Barber,
        AdminBookingService Service,
        AdminBookingSalon Salon);

    private sealed record AdminBookingCustomer(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        string? PhoneNumber);

    private sealed record AdminBookingBarber(Guid ProfileId, string DisplayName);

    private sealed record AdminBookingService(string Name);

    private sealed record AdminBookingSalon(
        Guid Id,
        string Name,
        string Address,
        string City,
        string TimeZoneId);
}
