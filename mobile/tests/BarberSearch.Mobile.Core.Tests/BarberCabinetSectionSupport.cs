using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Notifications;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Api.BarberRequests;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Данные остальных разделов кабинета: расписание, карточка, салоны, онбординг, уведомления.
/// </summary>
/// <remarks>
/// Дополняет <see cref="BarberCabinetFixtures"/>, не меняя его: первый срез тестов уже
/// пользуется теми данными, и переносить их значило бы править чужие тесты. Здесь только
/// то, чего не было, — ответы расписания, собственная карточка, список салонов, каталог,
/// заявка и инбокс.
/// </remarks>
internal static class BarberCabinetData
{
    /// <summary>
    /// Идентификатор профиля, который вернёт создание в онбординге.
    /// </summary>
    public static readonly Guid CreatedProfileId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000f1");

    public static readonly Guid SpecializationId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000a1");

    public static readonly Guid SecondSpecializationId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000a2");

    public static readonly Guid SecondHairServiceId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000a3");

    public static readonly Guid TimeOffId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000a4");

    public static readonly Guid NotificationId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000a5");

    public static readonly Guid SecondNotificationId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000a6");

    public static readonly Guid RequestId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000a7");

    public const string ConflictCode = "barberSchedule.conflict";

    /// <summary>
    /// Один день недельного шаблона.
    /// </summary>
    /// <param name="dayOfWeek">День недели.</param>
    /// <param name="isDayOff">Выходной ли день.</param>
    /// <param name="startHour">Начало работы.</param>
    /// <param name="endHour">Окончание работы.</param>
    /// <returns>День ответа расписания.</returns>
    public static BarberScheduleDayResponse Day(
        DayOfWeek dayOfWeek,
        bool isDayOff = false,
        int startHour = 10,
        int endHour = 20) =>
        new(
            dayOfWeek,
            isDayOff,
            new TimeOnly(startHour, 0),
            new TimeOnly(endHour, 0));

    /// <summary>
    /// Неделя по умолчанию: будни 10:00–20:00, суббота и воскресенье — выходные.
    /// </summary>
    /// <returns>Семь дней, понедельник первый.</returns>
    public static IReadOnlyList<BarberScheduleDayResponse> Week() =>
    [
        Day(DayOfWeek.Monday),
        Day(DayOfWeek.Tuesday),
        Day(DayOfWeek.Wednesday),
        Day(DayOfWeek.Thursday),
        Day(DayOfWeek.Friday),
        Day(DayOfWeek.Saturday, isDayOff: true),
        Day(DayOfWeek.Sunday, isDayOff: true),
    ];

    /// <summary>
    /// Неделя второго салона: часы сдвинуты, воскресенье рабочее.
    /// </summary>
    /// <returns>Семь дней, понедельник первый.</returns>
    public static IReadOnlyList<BarberScheduleDayResponse> OtherWeek() =>
    [
        Day(DayOfWeek.Monday, startHour: 12, endHour: 21),
        Day(DayOfWeek.Tuesday, startHour: 12, endHour: 21),
        Day(DayOfWeek.Wednesday, startHour: 12, endHour: 21),
        Day(DayOfWeek.Thursday, startHour: 12, endHour: 21),
        Day(DayOfWeek.Friday, startHour: 12, endHour: 21),
        Day(DayOfWeek.Saturday, isDayOff: true),
        Day(DayOfWeek.Sunday, startHour: 12, endHour: 18),
    ];

    /// <summary>
    /// Салон в ответе расписания.
    /// </summary>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="name">Название.</param>
    /// <param name="city">Город.</param>
    /// <param name="timeZoneId">Пояс салона.</param>
    /// <param name="isPrimary">Основной ли салон.</param>
    /// <param name="weekly">Недельный шаблон.</param>
    /// <param name="exceptions">Исключения на даты.</param>
    /// <returns>Салон ответа расписания.</returns>
    public static BarberScheduleSalonResponse ScheduleSalon(
        Guid? salonId = null,
        string name = "Hairo",
        string city = "Москва",
        string timeZoneId = BarberCabinetFixtures.Moscow,
        bool isPrimary = true,
        IReadOnlyList<BarberScheduleDayResponse>? weekly = null,
        IReadOnlyList<BarberScheduleExceptionResponse>? exceptions = null) =>
        new(
            salonId ?? BarberCabinetFixtures.SalonId,
            name,
            city,
            timeZoneId,
            isPrimary,
            weekly ?? Week(),
            exceptions ?? []);

    /// <summary>
    /// Ответ расписания.
    /// </summary>
    /// <param name="salons">Салоны профиля.</param>
    /// <param name="timeOffs">Отпуска профиля.</param>
    /// <returns>Расписание для раздела.</returns>
    public static BarberScheduleResponse Schedule(
        IReadOnlyList<BarberScheduleSalonResponse>? salons = null,
        IReadOnlyList<BarberScheduleTimeOffResponse>? timeOffs = null) =>
        new(
            BarberCabinetFixtures.BarberProfileId,
            salons ?? [ScheduleSalon()],
            timeOffs ?? []);

    /// <summary>
    /// Исключение на дату.
    /// </summary>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="isDayOff">Выходной ли день.</param>
    /// <param name="startHour">Начало своих часов.</param>
    /// <param name="endHour">Окончание своих часов.</param>
    /// <param name="reason">Пометка мастера.</param>
    /// <returns>Исключение ответа расписания.</returns>
    public static BarberScheduleExceptionResponse Exception(
        DateOnly date,
        bool isDayOff = true,
        int startHour = 10,
        int endHour = 14,
        string? reason = null) =>
        new(
            date,
            isDayOff,
            isDayOff ? TimeOnly.MinValue : new TimeOnly(startHour, 0),
            isDayOff ? TimeOnly.MinValue : new TimeOnly(endHour, 0),
            reason);

    /// <summary>
    /// Отпуск: по умолчанию 15–17 ноября в Москве.
    /// </summary>
    /// <param name="id">Идентификатор периода.</param>
    /// <param name="startAt">Начало в UTC.</param>
    /// <param name="endAt">Окончание в UTC, исключающее.</param>
    /// <param name="reason">Пометка мастера.</param>
    /// <returns>Отпуск ответа расписания.</returns>
    public static BarberScheduleTimeOffResponse TimeOff(
        Guid? id = null,
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null,
        string? reason = null) =>
        new(
            id ?? TimeOffId,
            startAt ?? new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.Zero),
            endAt ?? new DateTimeOffset(2026, 11, 17, 21, 0, 0, TimeSpan.Zero),
            reason);

    /// <summary>
    /// Затронутый визит в ответе <c>409</c>.
    /// </summary>
    /// <param name="id">Идентификатор брони.</param>
    /// <param name="customerName">Имя клиента.</param>
    /// <param name="startAt">Начало визита.</param>
    /// <returns>Визит для диалога конфликта.</returns>
    public static ScheduleConflictResponse Conflict(
        Guid? id = null,
        string customerName = "Иван Петров",
        DateTimeOffset? startAt = null)
    {
        var start = startAt ?? BarberCabinetFixtures.Now.AddHours(2);

        return new ScheduleConflictResponse(
            id ?? BarberCabinetFixtures.BookingId,
            start,
            start.AddHours(1),
            BarberCabinetFixtures.Moscow,
            customerName,
            "Стрижка",
            BookingStatus.Confirmed);
    }

    /// <summary>
    /// <c>409 barberSchedule.conflict</c> с затронутыми визитами.
    /// </summary>
    /// <param name="conflicts">Визиты из расширения <c>conflicts</c>.</param>
    /// <returns>Исключение API для фейка.</returns>
    public static BarberSearchApiException ConflictError(
        params ScheduleConflictResponse[] conflicts) =>
        new(
            System.Net.HttpStatusCode.Conflict,
            "Request failed.",
            [ConflictCode],
            new Dictionary<string, string[]>(),
            conflicts);

    /// <summary>
    /// Собственная карточка мастера.
    /// </summary>
    /// <param name="displayName">Имя.</param>
    /// <param name="grade">Грейд.</param>
    /// <param name="bio">Описание.</param>
    /// <param name="yearsOfExperience">Стаж.</param>
    /// <param name="languages">Языки.</param>
    /// <param name="rating">Рейтинг.</param>
    /// <param name="reviewsCount">Число отзывов.</param>
    /// <param name="paymentMethods">Способы оплаты.</param>
    /// <param name="latePolicy">Правило опозданий.</param>
    /// <param name="isActive">Профиль активен.</param>
    /// <param name="isVerified">Профиль подтверждён.</param>
    /// <param name="services">Прайс.</param>
    /// <param name="specializations">Специализации.</param>
    /// <param name="additionalServices">Дополнительные услуги.</param>
    /// <param name="certificates">Сертификаты и обучение.</param>
    /// <param name="achievements">Достижения.</param>
    /// <param name="photos">Фотографии портфолио.</param>
    /// <returns>Карточка для раздела и формы правки.</returns>
    public static BarberProfileResponse Profile(
        string displayName = "Тимур Мастеров",
        Grade grade = Grade.SeniorBarber,
        string? bio = "Стригу с 2014 года.",
        int yearsOfExperience = 8,
        IReadOnlyList<Language>? languages = null,
        decimal? rating = 4.8m,
        int reviewsCount = 12,
        IReadOnlyList<PaymentMethod>? paymentMethods = null,
        string latePolicy = "Опаздывающих жду 10 минут.",
        bool isActive = true,
        bool isVerified = true,
        IReadOnlyList<BarberProfileServiceResponse>? services = null,
        IReadOnlyList<BarberProfileSpecializationResponse>? specializations = null,
        IReadOnlyList<string>? additionalServices = null,
        IReadOnlyList<BarberProfilePhotoResponse>? photos = null,
        IReadOnlyList<string>? certificates = null,
        IReadOnlyList<string>? achievements = null) =>
        new(
            BarberCabinetFixtures.BarberProfileId,
            BarberCabinetFixtures.CustomerId,
            null,
            displayName,
            grade,
            bio,
            yearsOfExperience,
            languages ?? [Language.Ru, Language.En],
            certificates ?? [],
            rating,
            reviewsCount,
            achievements ?? [],
            paymentMethods ?? [PaymentMethod.Cash, PaymentMethod.Sbp],
            [],
            [],
            latePolicy,
            isActive,
            isVerified,
            services ?? [BarberCabinetFixtures.Service()],
            [BarberCabinetFixtures.Salon()],
            specializations ?? [],
            additionalServices ?? [],
            photos ?? []);

    /// <summary>
    /// Услуга справочника.
    /// </summary>
    /// <param name="id">Идентификатор услуги.</param>
    /// <param name="name">Название.</param>
    /// <param name="durationMinutes">Длительность по умолчанию.</param>
    /// <param name="category">Категория.</param>
    /// <returns>Строка справочника услуг.</returns>
    public static HairServiceResponse HairService(
        Guid? id = null,
        string name = "Стрижка",
        int durationMinutes = 30,
        ServiceCategory category = ServiceCategory.Haircut) =>
        new(id ?? BarberCabinetFixtures.HairServiceId, name, category, durationMinutes, null);

    /// <summary>
    /// Специализация справочника.
    /// </summary>
    /// <param name="id">Идентификатор.</param>
    /// <param name="name">Название.</param>
    /// <param name="description">Описание.</param>
    /// <returns>Строка справочника специализаций.</returns>
    public static SpecializationResponse Specialization(
        Guid? id = null,
        string name = "Фейд",
        string? description = null) =>
        new(id ?? SpecializationId, name, description);

    /// <summary>
    /// Специализация в карточке мастера.
    /// </summary>
    /// <param name="specializationId">Идентификатор справочника.</param>
    /// <param name="name">Название.</param>
    /// <returns>Строка карточки.</returns>
    public static BarberProfileSpecializationResponse ProfileSpecialization(
        Guid? specializationId = null,
        string name = "Фейд") =>
        new(
            Guid.Parse("9e100000-0000-4000-8000-0000000000b7"),
            specializationId ?? SpecializationId,
            name,
            null);

    /// <summary>
    /// Фотография портфолио.
    /// </summary>
    /// <param name="description">Подпись.</param>
    /// <param name="isPublished">Опубликована.</param>
    /// <param name="isBlocked">Скрыта администратором.</param>
    /// <param name="status">Допуск к публикации.</param>
    /// <param name="id">Идентификатор кадра; по умолчанию один и тот же — для одиночных проверок.</param>
    /// <returns>Фотография карточки.</returns>
    public static BarberProfilePhotoResponse Photo(
        string? description = null,
        bool isPublished = true,
        bool isBlocked = false,
        PhotoModerationStatus status = PhotoModerationStatus.Approved,
        Guid? id = null) =>
        new(
            id ?? Guid.Parse("9e100000-0000-4000-8000-0000000000b8"),
            "/media/photo.jpg",
            "/media/photo.jpg?size=md",
            description,
            0,
            isPublished,
            isBlocked,
            status,
            null);

    /// <summary>
    /// Салон в списке «Мои салоны».
    /// </summary>
    /// <param name="salonId">Идентификатор.</param>
    /// <param name="name">Название.</param>
    /// <param name="city">Город.</param>
    /// <param name="isPrimary">Основной ли салон.</param>
    /// <param name="status">Состояние проверки.</param>
    /// <param name="isListed">Показывается ли в каталоге.</param>
    /// <param name="rejectionReason">Причина отказа.</param>
    /// <param name="kind">Вид места приёма.</param>
    /// <returns>Привязанный салон.</returns>
    public static BarberSalonSummaryResponse SalonSummary(
        Guid? salonId = null,
        string name = "Hairo",
        string city = "Москва",
        bool isPrimary = false,
        SalonModerationStatus status = SalonModerationStatus.Active,
        bool isListed = true,
        string? rejectionReason = null,
        PlaceKind kind = PlaceKind.Salon) =>
        new(
            salonId ?? BarberCabinetFixtures.SalonId,
            name,
            city,
            "Ленина, 1",
            BarberCabinetFixtures.Moscow,
            isPrimary,
            "Россия",
            null,
            55.75,
            37.62,
            "+7 495 000-00-00",
            null,
            null,
            status,
            rejectionReason,
            kind,
            isListed);

    /// <summary>
    /// Салон публичного каталога.
    /// </summary>
    /// <param name="id">Идентификатор.</param>
    /// <param name="name">Название.</param>
    /// <param name="city">Город.</param>
    /// <param name="kind">Вид места приёма.</param>
    /// <returns>Строка каталога.</returns>
    public static SalonDirectoryItemResponse DirectorySalon(
        Guid? id = null,
        string name = "Hairo",
        string city = "Москва",
        PlaceKind kind = PlaceKind.Salon) =>
        new(id ?? BarberCabinetFixtures.OtherSalonId, name, city, "Ленина, 1", BarberCabinetFixtures.Moscow, kind);

    /// <summary>
    /// Уведомление инбокса.
    /// </summary>
    /// <param name="id">Идентификатор.</param>
    /// <param name="kind">Вид уведомления.</param>
    /// <param name="isRead">Прочитано ли оно.</param>
    /// <param name="createdAt">Когда создано.</param>
    /// <param name="startAt">Начало визита.</param>
    /// <param name="bookingsCount">Число записей у дайджеста.</param>
    /// <returns>Строка инбокса.</returns>
    public static BarberNotificationResponse Notification(
        Guid? id = null,
        BarberNotificationKind kind = BarberNotificationKind.Upcoming,
        bool isRead = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? startAt = null,
        int? bookingsCount = null)
    {
        var created = createdAt ?? BarberCabinetFixtures.Now.AddMinutes(-10);

        return new BarberNotificationResponse(
            id ?? NotificationId,
            kind,
            created,
            isRead ? created : null,
            kind == BarberNotificationKind.Upcoming ? BarberCabinetFixtures.BookingId : null,
            startAt ?? created.AddMinutes(30),
            "Стрижка",
            "Hairo",
            BarberCabinetFixtures.Moscow,
            bookingsCount,
            BarberNotificationDelivery.Sent);
    }

    /// <summary>
    /// Заявка на роль мастера.
    /// </summary>
    /// <param name="status">Статус заявки.</param>
    /// <param name="displayName">Имя из заявки.</param>
    /// <param name="yearsOfExperience">Опыт из заявки.</param>
    /// <param name="about">Рассказ о себе.</param>
    /// <returns>Последняя заявка пользователя.</returns>
    public static BarberRequestResponse Request(
        BarberRequestStatus status = BarberRequestStatus.Approved,
        string displayName = "Тимур Мастеров",
        int yearsOfExperience = 8,
        string? about = "Стригу с 2014 года.") =>
        new(
            RequestId,
            status,
            displayName,
            yearsOfExperience,
            "Москва",
            about,
            false,
            new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
            status == BarberRequestStatus.Pending
                ? null
                : new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero),
            status == BarberRequestStatus.Rejected ? "Мало опыта" : null);

    /// <summary>
    /// Ответ сервера на продление пары.
    /// </summary>
    /// <param name="accessToken">Новый access-токен.</param>
    /// <param name="refreshToken">Новый refresh-токен.</param>
    /// <returns>Пара токенов.</returns>
    public static TokenResponse Tokens(
        string accessToken = "new-access",
        string refreshToken = "new-refresh") =>
        new(accessToken, "Bearer", 3600, refreshToken, 1_209_600);
}

/// <summary>
/// Запись недельного шаблона, ушедшая на сервер.
/// </summary>
/// <param name="ProfileId">Профиль мастера.</param>
/// <param name="SalonId">Салон пары.</param>
/// <param name="Request">Тело замены недели.</param>
/// <param name="Force">Повтор после подтверждения конфликта.</param>
internal sealed record WeekWrite(
    Guid ProfileId,
    Guid SalonId,
    ReplaceWorkingSchedulesRequest Request,
    bool Force);

/// <summary>
/// Запись исключения на дату, ушедшая на сервер.
/// </summary>
/// <param name="ProfileId">Профиль мастера.</param>
/// <param name="SalonId">Салон пары.</param>
/// <param name="Date">Локальная дата салона.</param>
/// <param name="Request">Тело исключения.</param>
/// <param name="Force">Повтор после подтверждения конфликта.</param>
internal sealed record ExceptionWrite(
    Guid ProfileId,
    Guid SalonId,
    DateOnly Date,
    SetScheduleExceptionRequest Request,
    bool Force);

/// <summary>
/// Запись отпуска, ушедшая на сервер.
/// </summary>
/// <param name="ProfileId">Профиль мастера.</param>
/// <param name="Request">Тело отпуска.</param>
/// <param name="Force">Повтор после подтверждения конфликта.</param>
internal sealed record TimeOffWrite(
    Guid ProfileId,
    AddBarberProfileTimeOffRequest Request,
    bool Force);

/// <summary>
/// Привязка салона, ушедшая на сервер.
/// </summary>
/// <param name="ProfileId">Профиль мастера.</param>
/// <param name="SalonId">Салон каталога.</param>
/// <param name="IsPrimary">Сделать салон основным.</param>
internal sealed record SalonAttach(Guid ProfileId, Guid SalonId, bool IsPrimary);

/// <summary>
/// Подменённый клиент расписания: запоминает записи и отдаёт заданные ответы.
/// </summary>
internal sealed class FakeBarberScheduleApi : IBarberScheduleApi
{
    public Func<Guid, Task<BarberScheduleResponse>> Get { get; set; } =
        static _ => Task.FromResult(BarberCabinetData.Schedule());

    public Func<Guid, Guid, ReplaceWorkingSchedulesRequest, bool, Task> Replace { get; set; } =
        static (_, _, _, _) => Task.CompletedTask;

    public Func<Guid, Guid, DateOnly, SetScheduleExceptionRequest, bool, Task> SetException { get; set; } =
        static (_, _, _, _, _) => Task.CompletedTask;

    public Func<Guid, Guid, DateOnly, Task> RemoveException { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, AddBarberProfileTimeOffRequest, bool, Task> AddTimeOff { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> RemoveTimeOff { get; set; } =
        static (_, _) => Task.CompletedTask;

    public int GetCalls { get; private set; }

    public List<WeekWrite> WeekWrites { get; } = [];

    public List<ExceptionWrite> ExceptionWrites { get; } = [];

    public List<(Guid ProfileId, Guid SalonId, DateOnly Date)> ExceptionRemovals { get; } = [];

    public List<TimeOffWrite> TimeOffWrites { get; } = [];

    public List<(Guid ProfileId, Guid TimeOffId)> TimeOffRemovals { get; } = [];

    public async Task<BarberScheduleResponse> GetAsync(
        Guid barberProfileId,
        DateOnly? from = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        GetCalls++;

        return await Get(barberProfileId);
    }

    public async Task ReplaceWorkingSchedulesAsync(
        Guid barberProfileId,
        Guid salonId,
        ReplaceWorkingSchedulesRequest request,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        WeekWrites.Add(new WeekWrite(barberProfileId, salonId, request, force));

        await Replace(barberProfileId, salonId, request, force);
    }

    public async Task SetScheduleExceptionAsync(
        Guid barberProfileId,
        Guid salonId,
        DateOnly exceptionDate,
        SetScheduleExceptionRequest request,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ExceptionWrites.Add(new ExceptionWrite(barberProfileId, salonId, exceptionDate, request, force));

        await SetException(barberProfileId, salonId, exceptionDate, request, force);
    }

    public async Task RemoveScheduleExceptionAsync(
        Guid barberProfileId,
        Guid salonId,
        DateOnly exceptionDate,
        CancellationToken cancellationToken = default)
    {
        ExceptionRemovals.Add((barberProfileId, salonId, exceptionDate));

        await RemoveException(barberProfileId, salonId, exceptionDate);
    }

    public async Task AddTimeOffAsync(
        Guid barberProfileId,
        AddBarberProfileTimeOffRequest request,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        TimeOffWrites.Add(new TimeOffWrite(barberProfileId, request, force));

        await AddTimeOff(barberProfileId, request, force);
    }

    public async Task RemoveTimeOffAsync(
        Guid barberProfileId,
        Guid timeOffId,
        CancellationToken cancellationToken = default)
    {
        TimeOffRemovals.Add((barberProfileId, timeOffId));

        await RemoveTimeOff(barberProfileId, timeOffId);
    }
}

/// <summary>
/// Подменённый клиент собственного профиля: карточка, салоны, услуги и специализации.
/// </summary>
internal sealed class FakeBarberProfileApi : IBarberProfileApi
{
    public Func<Guid, Task<BarberProfileResponse>> Own { get; set; } =
        static _ => Task.FromResult(BarberCabinetData.Profile());

    public Func<BarberProfileCreateRequest, Task<Guid>> Create { get; set; } =
        static _ => Task.FromResult(BarberCabinetData.CreatedProfileId);

    public Func<Guid, BarberProfileUpdateRequest, Task> Update { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Task<IReadOnlyList<BarberSalonSummaryResponse>>> Salons { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<BarberSalonSummaryResponse>>([]);

    public Func<Guid, Guid, bool, Task> AddSalon { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> RemoveSalon { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> SetPrimarySalon { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, AddBarberProfileServiceRequest, Task> AddService { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Guid, UpdateBarberProfileServiceRequest, Task> UpdateService { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> RemoveService { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> AddSpecialization { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> RemoveSpecialization { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, PortfolioPhotoUpload, Task<Guid>> UploadPhoto { get; set; } =
        static (_, _) => Task.FromResult(Guid.Parse("9e100000-0000-4000-8000-0000000000c8"));

    public Func<Guid, Guid, Task> RemovePhoto { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Guid, string?, Task> UpdatePhotoDescription { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, IReadOnlyList<Guid>, Task> ReorderPhotos { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> PublishPhoto { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, Guid, Task> UnpublishPhoto { get; set; } =
        static (_, _) => Task.CompletedTask;

    public int OwnCalls { get; private set; }

    public int SalonCalls { get; private set; }

    public List<BarberProfileCreateRequest> CreateRequests { get; } = [];

    public List<(Guid ProfileId, BarberProfileUpdateRequest Request)> UpdateRequests { get; } = [];

    public List<SalonAttach> SalonAttaches { get; } = [];

    public List<(Guid ProfileId, Guid SalonId)> PrimaryRequests { get; } = [];

    public List<(Guid ProfileId, Guid HairServiceId)> ServiceRemovals { get; } = [];

    public List<(Guid ProfileId, AddBarberProfileServiceRequest Request)> ServiceAdds { get; } = [];

    public List<(Guid ProfileId, Guid HairServiceId, UpdateBarberProfileServiceRequest Request)> ServiceUpdates { get; } = [];

    public List<(Guid ProfileId, Guid SpecializationId)> SpecializationAdds { get; } = [];

    public List<(Guid ProfileId, Guid SpecializationId)> SpecializationRemovals { get; } = [];

    public List<(Guid ProfileId, PortfolioPhotoUpload Photo)> PhotoUploads { get; } = [];

    public List<(Guid ProfileId, Guid PhotoId)> PhotoRemovals { get; } = [];

    public List<(Guid ProfileId, Guid PhotoId, string? Description)> PhotoDescriptions { get; } = [];

    public List<(Guid ProfileId, IReadOnlyList<Guid> PhotoIds)> PhotoReorders { get; } = [];

    public List<(Guid ProfileId, Guid PhotoId)> PhotoPublications { get; } = [];

    public List<(Guid ProfileId, Guid PhotoId)> PhotoUnpublications { get; } = [];

    public async Task<BarberProfileResponse> GetOwnAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        OwnCalls++;

        return await Own(barberProfileId);
    }

    public async Task<Guid> CreateAsync(
        BarberProfileCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        CreateRequests.Add(request);

        return await Create(request);
    }

    public async Task UpdateAsync(
        Guid barberProfileId,
        BarberProfileUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        UpdateRequests.Add((barberProfileId, request));

        await Update(barberProfileId, request);
    }

    public async Task<IReadOnlyList<BarberSalonSummaryResponse>> GetSalonsAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        SalonCalls++;

        return await Salons(barberProfileId);
    }

    public async Task AddSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        bool isPrimary,
        CancellationToken cancellationToken = default)
    {
        SalonAttaches.Add(new SalonAttach(barberProfileId, salonId, isPrimary));

        await AddSalon(barberProfileId, salonId, isPrimary);
    }

    public async Task RemoveSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        await RemoveSalon(barberProfileId, salonId);
    }

    public async Task SetPrimarySalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        PrimaryRequests.Add((barberProfileId, salonId));

        await SetPrimarySalon(barberProfileId, salonId);
    }

    public async Task AddServiceAsync(
        Guid barberProfileId,
        AddBarberProfileServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ServiceAdds.Add((barberProfileId, request));

        await AddService(barberProfileId, request);
    }

    public async Task UpdateServiceAsync(
        Guid barberProfileId,
        Guid hairServiceId,
        UpdateBarberProfileServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ServiceUpdates.Add((barberProfileId, hairServiceId, request));

        await UpdateService(barberProfileId, hairServiceId, request);
    }

    public async Task RemoveServiceAsync(
        Guid barberProfileId,
        Guid hairServiceId,
        CancellationToken cancellationToken = default)
    {
        ServiceRemovals.Add((barberProfileId, hairServiceId));

        await RemoveService(barberProfileId, hairServiceId);
    }

    public async Task AddSpecializationAsync(
        Guid barberProfileId,
        Guid specializationId,
        CancellationToken cancellationToken = default)
    {
        SpecializationAdds.Add((barberProfileId, specializationId));

        await AddSpecialization(barberProfileId, specializationId);
    }

    public async Task RemoveSpecializationAsync(
        Guid barberProfileId,
        Guid specializationId,
        CancellationToken cancellationToken = default)
    {
        SpecializationRemovals.Add((barberProfileId, specializationId));

        await RemoveSpecialization(barberProfileId, specializationId);
    }

    public async Task<Guid> UploadPhotoAsync(
        Guid barberProfileId,
        PortfolioPhotoUpload photo,
        CancellationToken cancellationToken = default)
    {
        PhotoUploads.Add((barberProfileId, photo));

        return await UploadPhoto(barberProfileId, photo);
    }

    public async Task RemovePhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default)
    {
        PhotoRemovals.Add((barberProfileId, photoId));

        await RemovePhoto(barberProfileId, photoId);
    }

    public async Task UpdatePhotoDescriptionAsync(
        Guid barberProfileId,
        Guid photoId,
        string? description,
        CancellationToken cancellationToken = default)
    {
        PhotoDescriptions.Add((barberProfileId, photoId, description));

        await UpdatePhotoDescription(barberProfileId, photoId, description);
    }

    public async Task ReorderPhotosAsync(
        Guid barberProfileId,
        IReadOnlyList<Guid> photoIds,
        CancellationToken cancellationToken = default)
    {
        PhotoReorders.Add((barberProfileId, photoIds));

        await ReorderPhotos(barberProfileId, photoIds);
    }

    public async Task PublishPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default)
    {
        PhotoPublications.Add((barberProfileId, photoId));

        await PublishPhoto(barberProfileId, photoId);
    }

    public async Task UnpublishPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default)
    {
        PhotoUnpublications.Add((barberProfileId, photoId));

        await UnpublishPhoto(barberProfileId, photoId);
    }
}

/// <summary>
/// Подменённый публичный справочник: специализации, услуги и каталог салонов.
/// </summary>
/// <remarks>
/// Остальные чтения витрины кабинету не нужны, поэтому они честно падают: если раздел
/// внезапно начнёт их звать, тест скажет об этом, а не вернёт пустоту.
/// </remarks>
internal sealed class FakeBarberSearchApi : IBarberSearchApi
{
    public Func<CancellationToken, Task<IReadOnlyList<SpecializationResponse>>> Specializations { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<SpecializationResponse>>([BarberCabinetData.Specialization()]);

    public Func<CancellationToken, Task<IReadOnlyList<HairServiceResponse>>> HairServices { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>([
            BarberCabinetData.HairService(),
            BarberCabinetData.HairService(BarberCabinetData.SecondHairServiceId, "Борода", 45, ServiceCategory.Beard),
        ]);

    public Func<string?, string?, CancellationToken, Task<IReadOnlyList<SalonDirectoryItemResponse>>> Salons { get; set; } =
        static (_, _, _) => Task.FromResult<IReadOnlyList<SalonDirectoryItemResponse>>([
            BarberCabinetData.DirectorySalon(),
        ]);

    public int SpecializationCalls { get; private set; }

    public int HairServiceCalls { get; private set; }

    public List<(string? City, string? Search)> SalonQueries { get; } = [];

    public async Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
        CancellationToken cancellationToken = default)
    {
        SpecializationCalls++;

        return await Specializations(cancellationToken);
    }

    public async Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
        CancellationToken cancellationToken = default)
    {
        HairServiceCalls++;

        return await HairServices(cancellationToken);
    }

    public async Task<IReadOnlyList<SalonDirectoryItemResponse>> SearchSalonsAsync(
        string? city,
        string? search,
        CancellationToken cancellationToken = default)
    {
        SalonQueries.Add((city, search));

        return await Salons(city, search, cancellationToken);
    }

    public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
        BarberSearchFilters filters,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не ищет мастеров.");

    public Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не подсказывает города.");

    public Task<BarberProfileResponse> GetBarberProfileAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет читает собственную карточку другим клиентом.");

    public Task<BarberSlotsResponse> GetSlotsAsync(
        Guid barberProfileId,
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не читает слоты витрины.");

    public Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
        Guid barberProfileId,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не читает отзывы.");
}

/// <summary>
/// Подменённый клиент заявок: онбордингу нужна только своя заявка.
/// </summary>
internal sealed class FakeBarberRequestsApi : IBarberRequestsApi
{
    public Func<CancellationToken, Task<MyBarberRequestResponse>> Mine { get; set; } =
        static _ => Task.FromResult(new MyBarberRequestResponse(null));

    public int MineCalls { get; private set; }

    public async Task<MyBarberRequestResponse> GetMineAsync(CancellationToken cancellationToken = default)
    {
        MineCalls++;

        return await Mine(cancellationToken);
    }

    public Task<BarberRequestResponse> CreateAsync(
        CreateBarberRequestRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Онбординг не создаёт заявку.");
}

/// <summary>
/// Подменённый клиент авторизации: онбордингу нужно только продление пары.
/// </summary>
internal sealed class FakeAuthApi : IAuthApi
{
    public Func<string, Task<TokenResponse>> Refresh { get; set; } =
        static _ => Task.FromResult(BarberCabinetData.Tokens());

    public List<string> RefreshRequests { get; } = [];

    public async Task<TokenResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        RefreshRequests.Add(refreshToken);

        return await Refresh(refreshToken);
    }

    public Task<Guid> RegisterAsync(
        BarberSearch.Contracts.Auth.Register.RegisterRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Онбординг не регистрирует аккаунт.");

    public Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Онбординг не сбрасывает пароль.");

    public Task<TokenResponse> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Онбординг не входит по паролю.");

    public Task LogoutAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Онбординг не выходит.");
}

/// <summary>
/// Подменённый инбокс: страницы, счётчик и две отметки прочтения.
/// </summary>
internal sealed class FakeBarberInboxApi : IBarberNotificationsApi
{
    public Func<string?, Task<PagedResponse<BarberNotificationResponse>>> Page { get; set; } =
        static _ => Task.FromResult(new PagedResponse<BarberNotificationResponse>([], null));

    public Func<Task<int>> UnreadCount { get; set; } = static () => Task.FromResult(0);

    public Func<Guid, Task> MarkRead { get; set; } = static _ => Task.CompletedTask;

    public Func<Task> MarkAllRead { get; set; } = static () => Task.CompletedTask;

    public List<(string? Cursor, int? PageSize)> PageRequests { get; } = [];

    public List<Guid> ReadIds { get; } = [];

    public int MarkAllCalls { get; private set; }

    public int UnreadCalls { get; private set; }

    public async Task<PagedResponse<BarberNotificationResponse>> GetAsync(
        string? cursor = null,
        int? pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        PageRequests.Add((cursor, pageSize));

        return await Page(cursor);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ReadIds.Add(id);

        await MarkRead(id);
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        MarkAllCalls++;

        await MarkAllRead();
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        UnreadCalls++;

        return await UnreadCount();
    }

    public Task<NotificationSettingsResponse> GetSettingsAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Инбокс не читает настройки напоминаний.");

    public Task<NotificationSettingsResponse> UpdateSettingsAsync(
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Инбокс не меняет настройки напоминаний.");

    public Task<TelegramLinkResponse> CreateTelegramLinkAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Инбокс не привязывает Telegram.");

    public Task UnlinkTelegramAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Инбокс не отвязывает Telegram.");
}
