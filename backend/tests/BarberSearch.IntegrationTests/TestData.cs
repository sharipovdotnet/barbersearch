using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Reviews;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Specializations;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Сидирование через реальный <see cref="ApplicationDbContext"/>: доменные фабрики, никаких
/// SQL-вставок «в обход» модели.
/// </summary>
/// <remarks>
/// Рейтинг — исключение: его меняет только публикация отзыва через Application-обработчик,
/// а сценарию поиска нужен управляемый набор значений. Точечный
/// <c>UPDATE barber_profiles SET rating = …</c> допустим и не обходит проверки схемы
/// (rating numeric(3,2), ограничение диапазона остаются в силе).
/// </remarks>
internal sealed class TestData
{
    private readonly TestApp _app;

    public TestData(TestApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _app = app;
    }

    /// <summary>
    /// Создаёт пользователя-барбера и его профиль (активный, подтверждённый).
    /// </summary>
    /// <remarks>
    /// <paramref name="languages"/> по умолчанию <c>null</c>: домен подставляет <c>Ru</c>,
    /// а существующие вызовы не меняются. Явный список нужен тестам фильтра по языку.
    /// </remarks>
    public async Task<BarberProfile> CreateBarberProfileAsync(
        string displayName,
        string? email = null,
        List<Language>? languages = null)
    {
        var user = await _app.CreateUserAsync(
            email ?? $"barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = Require(BarberProfile.Create(
            userId: user.Id,
            displayName: displayName,
            yearsOfExperience: 5,
            bio: null,
            languages: languages,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null));

        Require(profile.Verify());

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return profile;
    }

    /// <summary>
    /// Создаёт барбера, салон и услугу — всё, что нужно для <c>POST api/bookings</c>.
    /// </summary>
    public async Task<SeededBarber> CreateBookableBarberAsync(
        string displayName,
        string city = "Berlin",
        string? email = null)
    {
        var user = await _app.CreateUserAsync(
            email ?? $"barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var salon = Require(Salon.Create(
            name: $"Salon {displayName}",
            address: "Teststrasse 1",
            city: city,
            country: "Germany",
            timeZoneId: "Europe/Berlin",
            latitude: 52.5200,
            longitude: 13.4050));

        var hairService = Require(HairService.Create(
            name: $"Haircut {displayName}",
            category: ServiceCategory.Haircut,
            defaultDurationMinutes: 30));

        db.Salons.Add(salon);
        db.HairServices.Add(hairService);
        await db.SaveChangesAsync();

        var profile = Require(BarberProfile.Create(
            userId: user.Id,
            displayName: displayName,
            yearsOfExperience: 5,
            bio: null,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null));

        Require(profile.Verify());
        Require(profile.AddSalon(salon.Id, isPrimary: true));
        Require(profile.AddService(hairService.Id, price: 45.00m, durationMinutes: 30));

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return new SeededBarber(
            user,
            profile,
            salon,
            hairService,
            profile.Services.Single());
    }

    /// <summary>
    /// Создаёт барбера с двумя салонами: первый — основной. Нужно для проверки
    /// <c>SetPrimarySalon</c> и отложенного exclusion-ограничения (R-06).
    /// </summary>
    public async Task<SeededBarberWithSalons> CreateBarberWithSalonsAsync(
        string displayName,
        string email,
        string city = "Berlin")
    {
        var user = await _app.CreateUserAsync(email, UserRole.Barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var primary = Require(Salon.Create(
            name: $"{displayName} Primary",
            address: "Teststrasse 1",
            city: city,
            country: "Germany",
            timeZoneId: "Europe/Berlin",
            latitude: 52.5200,
            longitude: 13.4050));

        var secondary = Require(Salon.Create(
            name: $"{displayName} Secondary",
            address: "Teststrasse 2",
            city: city,
            country: "Germany",
            timeZoneId: "Europe/Berlin",
            latitude: 52.5300,
            longitude: 13.4150));

        db.Salons.AddRange(primary, secondary);
        await db.SaveChangesAsync();

        var profile = Require(BarberProfile.Create(
            userId: user.Id,
            displayName: displayName,
            yearsOfExperience: 5,
            bio: null,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null));

        Require(profile.Verify());
        Require(profile.AddSalon(primary.Id, isPrimary: true));
        Require(profile.AddSalon(secondary.Id));

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return new SeededBarberWithSalons(user, profile, primary, secondary);
    }

    /// <summary>
    /// Создаёт завершённую бронь и отзыв по ней; <paramref name="published"/> = <c>true</c>
    /// публикует отзыв сразу (для негативного кейса «скрытие без токена не сработало»).
    /// </summary>
    /// <remarks>
    /// Бронь завершена доменными методами, а не SQL: отзыв можно создать только по
    /// состоявшемуся визиту, и тест не должен обходить это правило.
    /// </remarks>
    public async Task<Guid> CreateReviewAsync(
        SeededBarber barber,
        Guid customerId,
        int rating = 5,
        bool published = false)
    {
        ArgumentNullException.ThrowIfNull(barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var startAt = DateTimeOffset.UtcNow.AddDays(-2);
        var booking = Require(Booking.Create(
            customerId,
            barber.Profile.Id,
            barber.Service.Id,
            barber.Salon.Id,
            startAt,
            startAt.AddMinutes(30),
            45m,
            $"review-seed-{Guid.NewGuid():N}"));

        Require(booking.Confirm());
        Require(booking.Complete());

        var review = Require(Review.Create(booking, customerId, rating, "Seeded review"));

        if (published)
        {
            Require(review.Publish());
        }

        db.Bookings.Add(booking);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        return review.Id;
    }

    /// <summary>
    /// Создаёт заблокированного пользователя — цель для <c>POST api/users/{id}/activation</c>.
    /// </summary>
    public async Task<User> CreateBlockedUserAsync(string email)
    {
        var created = await _app.CreateUserAsync(email);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Загружаем в этом контексте: xmin — shadow-свойство, и у сущности из чужого
        // контекста его значения нет, поэтому UPDATE с optimistic concurrency дал бы 0 строк.
        var user = await db.Users.SingleAsync(candidate => candidate.Id == created.Id);

        Require(user.Deactivate());
        await db.SaveChangesAsync();

        return user;
    }

    /// <summary>
    /// Добавляет барберу активный салон в указанном городе — для фильтра по городу.
    /// </summary>
    public async Task<Salon> AttachSalonAsync(Guid barberProfileId, string city)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var salon = Require(Salon.Create(
            name: $"Salon {Guid.NewGuid():N}",
            address: "Teststrasse 1",
            city: city,
            country: "Germany",
            timeZoneId: "Europe/Berlin",
            latitude: 52.5200,
            longitude: 13.4050));

        var profile = await db.BarberProfiles
            .Include(x => x.Salons)
            .FirstAsync(x => x.Id == barberProfileId);

        db.Salons.Add(salon);
        Require(profile.AddSalon(salon.Id, isPrimary: true));
        await db.SaveChangesAsync();

        return salon;
    }

    /// <summary>
    /// Создаёт строку справочника специализаций; <paramref name="isActive"/> = <c>false</c>
    /// нужен, чтобы проверить «только активные».
    /// </summary>
    public async Task<Specialization> CreateSpecializationAsync(
        string name,
        bool isActive = true)
    {
        var specialization = Require(Specialization.Create(name, $"About {name}"));

        if (!isActive)
        {
            Require(specialization.Deactivate());
        }

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Specializations.Add(specialization);
        await db.SaveChangesAsync();

        return specialization;
    }

    /// <summary>
    /// Создаёт строку справочника услуг; <paramref name="isActive"/> = <c>false</c> нужен,
    /// чтобы проверить «только активные».
    /// </summary>
    public async Task<HairService> CreateHairServiceAsync(
        string name,
        ServiceCategory category = ServiceCategory.Haircut,
        int defaultDurationMinutes = 30,
        bool isActive = true)
    {
        var hairService = Require(HairService.Create(
            name,
            category,
            defaultDurationMinutes,
            $"About {name}"));

        if (!isActive)
        {
            Require(hairService.Deactivate());
        }

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.HairServices.Add(hairService);
        await db.SaveChangesAsync();

        return hairService;
    }

    /// <summary>
    /// Создаёт строку справочника салонов; <paramref name="isActive"/> = <c>false</c> нужен,
    /// чтобы проверить фильтр статуса админского каталога.
    /// </summary>
    public async Task<Salon> CreateSalonAsync(
        string name,
        string city = "Berlin",
        string address = "Teststrasse 1",
        string country = "Germany",
        string timeZoneId = "Europe/Berlin",
        double latitude = 52.5200,
        double longitude = 13.4050,
        string? postalCode = null,
        string? phoneNumber = null,
        string? websiteUrl = null,
        bool isActive = true)
    {
        var salon = Require(Salon.Create(
            name: name,
            address: address,
            city: city,
            country: country,
            timeZoneId: timeZoneId,
            latitude: latitude,
            longitude: longitude,
            postalCode: postalCode,
            phoneNumber: phoneNumber,
            websiteUrl: websiteUrl));

        if (!isActive)
        {
            Require(salon.Deactivate());
        }

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        return salon;
    }

    /// <summary>
    /// Привязывает существующий салон к профилю через доменный метод.
    /// </summary>
    public async Task AttachSalonAsync(Guid barberProfileId, Guid salonId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles
            .Include(x => x.Salons)
            .FirstAsync(x => x.Id == barberProfileId);

        Require(profile.AddSalon(salonId));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Переводит профиль в заданное состояние подтверждения и активности доменными методами.
    /// </summary>
    public async Task SetBarberProfileStateAsync(
        Guid barberProfileId,
        bool isVerified,
        bool isActive)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles.FirstAsync(x => x.Id == barberProfileId);

        Require(isVerified ? profile.Verify() : profile.Unverify());
        Require(isActive ? profile.Activate() : profile.Deactivate());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Выключает связь «барбер — услуга»: <c>usageCount</c> обязан её не учитывать.
    /// </summary>
    public async Task DeactivateServiceLinkAsync(Guid barberProfileId, Guid hairServiceId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles
            .Include(x => x.Services)
            .FirstAsync(x => x.Id == barberProfileId);

        var link = profile.Services.Single(x => x.HairServiceId == hairServiceId);

        Require(link.Deactivate());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Число связей «барбер — услуга» у строки справочника: после выключения строки они
    /// остаются, и «Выключить» ничего не каскадит.
    /// </summary>
    public async Task<int> CountHairServiceLinksAsync(Guid hairServiceId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.BarberServices.CountAsync(link => link.HairServiceId == hairServiceId);
    }

    /// <summary>
    /// Привязывает специализацию к профилю через доменный метод — так же, как это делает
    /// кабинет барбера.
    /// </summary>
    public async Task AddSpecializationAsync(Guid barberProfileId, Guid specializationId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles
            .Include(x => x.BarberSpecializations)
            .FirstAsync(x => x.Id == barberProfileId);

        Require(profile.AddSpecialization(specializationId));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Привязывает услугу справочника к профилю с ценой и длительностью.
    /// </summary>
    public async Task AttachHairServiceAsync(
        Guid barberProfileId,
        Guid hairServiceId,
        decimal price = 40.00m,
        int durationMinutes = 30)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles
            .Include(x => x.Services)
            .FirstAsync(x => x.Id == barberProfileId);

        Require(profile.AddService(hairServiceId, price, durationMinutes));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Проставляет сохранённый рейтинг и число отзывов — так, как это сделал бы обработчик
    /// публикации отзыва.
    /// </summary>
    public async Task SetRatingAsync(Guid barberProfileId, decimal rating, int reviewsCount = 1)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE barber_profiles
            SET rating = {rating}, reviews_count = {reviewsCount}
            WHERE id = {barberProfileId}
            """);

        Assert.Equal(1, updated);
    }

    /// <summary>
    /// Читает связи «барбер — салон» без кэша контекста: используется после HTTP-запроса.
    /// </summary>
    public async Task<IReadOnlyList<BarberSalon>> GetSalonLinksAsync(Guid barberProfileId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.BarberSalons
            .AsNoTracking()
            .Where(link => link.BarberProfileId == barberProfileId)
            .OrderBy(link => link.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Вставляет строку журнала аудита напрямую SQL-запросом.
    /// </summary>
    /// <remarks>
    /// Строка аудита — инфраструктурная запись без доменных правил: <c>AuditTrail</c> пишет
    /// только реальные изменения через репозитории, а тесту нужны точные <c>occurred_at</c>,
    /// автор и <c>payload</c>. Вставка идёт через тот же <see cref="ApplicationDbContext"/>
    /// и тот же тип параметров, что и у приложения; <c>jsonb</c> приводится явно.
    /// </remarks>
    /// <param name="occurredAt">Момент фиксации изменения.</param>
    /// <param name="actorUserId">Автор; <c>null</c> — системная запись.</param>
    /// <param name="action">Действие вида <c>{entity}.{created|updated|deleted}</c>.</param>
    /// <param name="entityType">Стабильное имя типа сущности.</param>
    /// <param name="entityId">Идентификатор изменённой сущности.</param>
    /// <param name="payload">JSON с изменениями или значениями.</param>
    /// <returns>Идентификатор вставленной строки.</returns>
    public async Task<Guid> SeedAuditAsync(
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid entityId,
        string payload)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(payload);

        var id = Guid.CreateVersion7();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_log (id, occurred_at, actor_user_id, action, entity_type, entity_id, payload)
            VALUES ({id}, {occurredAt}, {actorUserId}, {action}, {entityType}, {entityId}, {payload}::jsonb)
            """);
        Assert.Equal(1, inserted);

        return id;
    }

    /// <summary>
    /// Число строк журнала аудита: тест «правки каталога не аудируются» сравнивает его до и после.
    /// </summary>
    public async Task<int> CountAuditRowsAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.AuditLogs.CountAsync();
    }

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
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
