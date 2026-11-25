using System.Net;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Общие данные и подмены для тестов кабинета мастера.
/// </summary>
/// <remarks>
/// Фейки живут в одном файле на пять классов: интерфейсы кабинета широкие (у записей десять
/// методов), и копия в каждом файле разошлась бы при первом же изменении контракта. Ни один
/// фейк не ходит в сеть и не спит: паузы <see cref="AutoRetryLoop"/> приходят делегатом,
/// а «сегодня» — из <see cref="FixedTimeProvider"/>.
/// </remarks>
internal static class BarberCabinetFixtures
{
    /// <summary>
    /// Фиксированное «сейчас»: 22:00 UTC 14 ноября — это уже 15 ноября в Москве, поэтому
    /// тест видит, что календарь строится в поясе салона, а не в UTC.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);

    public static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    public static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    public static readonly Guid OtherSalonId = Guid.Parse("9e100000-0000-4000-8000-000000000003");
    public static readonly Guid BarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000004");
    public static readonly Guid HairServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000005");
    public static readonly Guid ServiceSpecializationId = Guid.Parse("9e100000-0000-4000-8000-0000000000e8");
    public static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000b1");
    public static readonly Guid SecondBookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000b2");
    public static readonly Guid CustomerId = Guid.Parse("9e100000-0000-4000-8000-0000000000c1");
    public static readonly Guid SecondCustomerId = Guid.Parse("9e100000-0000-4000-8000-0000000000c2");

    public const string Moscow = "Europe/Moscow";

    /// <summary>
    /// Специализация услуги прайса: «Фейд» — по ней прайс группируется и подписывается.
    /// </summary>
    public const string ServiceSpecializationName = "Фейд";

    /// <summary>
    /// Услуга мастера: цена и длительность отличаются от справочных.
    /// </summary>
    /// <param name="price">Цена у мастера.</param>
    /// <param name="durationMinutes">Длительность у мастера.</param>
    /// <param name="specializationName">Специализация услуги; по умолчанию «Фейд».</param>
    /// <returns>Строка прайса.</returns>
    public static BarberProfileServiceResponse Service(
        decimal price = 2400m,
        int durationMinutes = 60,
        string specializationName = ServiceSpecializationName) =>
        new(
            BarberServiceId,
            HairServiceId,
            "Стрижка",
            ServiceCategory.Haircut,
            30,
            price,
            durationMinutes,
            true,
            ServiceSpecializationId,
            specializationName);

    /// <summary>
    /// Салон мастера с поясом IANA.
    /// </summary>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="name">Название.</param>
    /// <param name="timeZoneId">Пояс салона.</param>
    /// <param name="isPrimary">Основной ли салон.</param>
    /// <returns>Салон из карточки.</returns>
    public static BarberProfileSalonResponse Salon(
        Guid? salonId = null,
        string name = "Hairo",
        string timeZoneId = Moscow,
        bool isPrimary = true) =>
        new(
            Guid.Parse("9e100000-0000-4000-8000-0000000000d1"),
            salonId ?? SalonId,
            name,
            "Ленина, 1",
            "Москва",
            "Россия",
            timeZoneId,
            PlaceKind.Salon,
            isPrimary,
            []);

    /// <summary>
    /// Данные формы записи клиента.
    /// </summary>
    /// <param name="services">Услуги; по умолчанию одна.</param>
    /// <param name="salons">Салоны; по умолчанию один основной.</param>
    /// <returns>Ответ <c>booking-options</c>.</returns>
    public static BarberBookingOptionsResponse Options(
        IReadOnlyList<BarberProfileServiceResponse>? services = null,
        IReadOnlyList<BarberProfileSalonResponse>? salons = null) =>
        new(services ?? [Service()], salons ?? [Salon()]);

    /// <summary>
    /// Сетка слотов на салонную дату.
    /// </summary>
    /// <param name="day">Локальная дата салона.</param>
    /// <param name="timeZoneId">Пояс салона.</param>
    /// <param name="isWorkingDay">Рабочий ли день.</param>
    /// <param name="slots">Свободные слоты.</param>
    /// <returns>Ответ сетки.</returns>
    public static BarberSlotsResponse Slots(
        DateOnly day,
        string timeZoneId = Moscow,
        bool isWorkingDay = true,
        params BarberSlotResponse[] slots) =>
        new(
            BarberProfileId,
            SalonId,
            BarberServiceId,
            HairServiceId,
            day,
            timeZoneId,
            new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            60,
            isWorkingDay,
            slots);

    /// <summary>
    /// Запись клиента для списка и действий.
    /// </summary>
    /// <param name="id">Идентификатор брони.</param>
    /// <param name="status">Статус.</param>
    /// <param name="startAt">Начало визита.</param>
    /// <param name="endAt">Окончание визита.</param>
    /// <param name="price">Цена.</param>
    /// <param name="customerName">Имя клиента.</param>
    /// <param name="phoneNumber">Телефон клиента.</param>
    /// <param name="barberNote">Заметка мастера.</param>
    /// <param name="customerNote">Комментарий клиента.</param>
    /// <param name="timeZoneId">Пояс салона.</param>
    /// <param name="cancellationReason">Причина отмены.</param>
    /// <returns>Бронь из ответа API.</returns>
    public static BarberBookingResponse Booking(
        Guid? id = null,
        BookingStatus status = BookingStatus.Confirmed,
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null,
        decimal price = 2400m,
        string customerName = "Иван Петров",
        string? phoneNumber = "+7 900 000-00-00",
        string? barberNote = null,
        string? customerNote = null,
        string timeZoneId = Moscow,
        string? cancellationReason = null)
    {
        var start = startAt ?? Now.AddHours(2);

        return new BarberBookingResponse(
            id ?? BookingId,
            status,
            start,
            endAt ?? start.AddHours(1),
            price,
            customerNote,
            barberNote,
            cancellationReason,
            new BarberBookingCustomerResponse(customerName, phoneNumber, null),
            new BarberBookingServiceResponse("Стрижка"),
            new BarberBookingSalonResponse("Hairo", "Ленина, 1", "Москва", timeZoneId));
    }

    /// <summary>
    /// Строка списка записей из брони.
    /// </summary>
    /// <param name="booking">Бронь.</param>
    /// <param name="isUpcoming">Строка из «Ближайших».</param>
    /// <returns>Строка для списка.</returns>
    public static BarberBookingItem Item(
        BarberBookingResponse booking,
        bool isUpcoming = true) =>
        new(booking, isUpcoming, Now);

    /// <summary>
    /// Минимальный ответ <c>/me</c>.
    /// </summary>
    /// <param name="role">Роль из токена.</param>
    /// <param name="barberProfileId">Профиль мастера в токене.</param>
    /// <param name="email">Адрес текущего аккаунта.</param>
    /// <returns>Текущий пользователь.</returns>
    public static MeResponse Me(
        string role = "Barber",
        Guid? barberProfileId = null,
        string email = "master@example.com") =>
        new(CustomerId, email, role, barberProfileId ?? BarberProfileId, true);

    /// <summary>
    /// Клиент, найденный поиском.
    /// </summary>
    /// <param name="id">Идентификатор пользователя.</param>
    /// <param name="firstName">Имя.</param>
    /// <param name="lastName">Фамилия.</param>
    /// <returns>Строка подсказки.</returns>
    public static BarberClientResponse Client(
        Guid? id = null,
        string firstName = "Иван",
        string lastName = "Петров") =>
        new(id ?? CustomerId, firstName, lastName, "ivan@example.com", "+7 900 000-00-00");

    /// <summary>
    /// Момент в календарной дате устройства: «сегодня» ленты не зависит от пояса хоста.
    /// </summary>
    /// <param name="hour">Час местного времени.</param>
    /// <param name="minute">Минута.</param>
    /// <returns>Инстант с локальным смещением хоста.</returns>
    public static DateTimeOffset TodayAt(int hour, int minute = 0)
    {
        var local = TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.Local).DateTime;
        var day = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var value = day.AddHours(hour).AddMinutes(minute);

        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }
}

/// <summary>
/// Часы теста: «сегодня» и доступность действий не зависят от настоящего времени.
/// </summary>
/// <param name="now">Зафиксированный момент.</param>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Доменная ошибка API с кодом — так её строит настоящий клиент из ProblemDetails.
/// </summary>
internal static class BarberCabinetApiError
{
    /// <summary>
    /// Создаёт исключение с кодом ответа.
    /// </summary>
    /// <param name="statusCode">Код ответа.</param>
    /// <param name="codes">Доменные коды.</param>
    /// <returns>Исключение для фейка.</returns>
    public static BarberSearchApiException Create(HttpStatusCode statusCode, params string[] codes) =>
        new(statusCode, "Request failed.", codes, new Dictionary<string, string[]>());
}

/// <summary>
/// Циклы автоповтора без настоящих пауз.
/// </summary>
internal static class BarberCabinetRetry
{
    /// <summary>
    /// Каждая пауза проходит сразу: попытки идут подряд, тест ждёт только завершения.
    /// </summary>
    /// <returns>Быстрый цикл.</returns>
    public static AutoRetryLoop Fast() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Первая пауза ждёт внешний сигнал: видно состояние «цикл работает, попытки ещё нет».
    /// </summary>
    /// <param name="gate">Сигнал, разрешающий попытку.</param>
    /// <returns>Цикл с воротами.</returns>
    public static AutoRetryLoop Gated(Task gate) =>
        new((_, _) => gate, [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Первая пауза проходит сразу, вторая висит до остановки: видно состояние между попытками.
    /// </summary>
    /// <returns>Цикл, который останавливается после первой попытки.</returns>
    public static AutoRetryLoop StopAfterFirst()
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) => Interlocked.Increment(ref waits) == 1
                ? Task.CompletedTask
                : WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Ожидание, которое заканчивается только отменой: Stop проверяется без таймеров.
    /// </summary>
    /// <param name="cancellationToken">Токен цикла.</param>
    /// <returns>Задача до отмены.</returns>
    public static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
    }
}

/// <summary>
/// Подменённый клиент записей кабинета: запоминает запросы и отдаёт заданные ответы.
/// </summary>
internal sealed class FakeBarberBookingsApi : IBarberBookingsApi
{
    public Func<string?, string?, int?, CancellationToken, Task<PagedResponse<BarberBookingResponse>>> Page { get; set; } =
        static (_, _, _, _) => Task.FromResult(new PagedResponse<BarberBookingResponse>([], null));

    public Func<CreateBarberBookingRequest, CancellationToken, Task<Guid>> Create { get; set; } =
        static (_, _) => Task.FromException<Guid>(new InvalidOperationException("CreateAsync не настроен."));

    public Func<Guid, CancellationToken, Task> Confirm { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, CancellationToken, Task> Complete { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, CancellationToken, Task> MarkNoShow { get; set; } =
        static (_, _) => Task.CompletedTask;

    public Func<Guid, string?, CancellationToken, Task> Cancel { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, string, CancellationToken, Task> SaveNote { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, decimal, CancellationToken, Task> ChangePrice { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public Func<Guid, DateOnly, CancellationToken, Task<BarberSlotsResponse>> RescheduleSlots { get; set; } =
        static (_, day, _) => Task.FromResult(BarberCabinetFixtures.Slots(
            day,
            slots:
            [
                new BarberSlotResponse(
                    new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(3)),
                    new DateTimeOffset(day.ToDateTime(new TimeOnly(11, 0)), TimeSpan.FromHours(3))),
            ]));

    public Func<Guid, DateTimeOffset, CancellationToken, Task> Reschedule { get; set; } =
        static (_, _, _) => Task.CompletedTask;

    public List<PageRequest> PageRequests { get; } = [];

    public List<CreateBarberBookingRequest> CreateRequests { get; } = [];

    public List<Guid> ConfirmRequests { get; } = [];

    public List<Guid> CompleteRequests { get; } = [];

    public List<Guid> NoShowRequests { get; } = [];

    public List<(Guid BookingId, string? Reason)> CancelRequests { get; } = [];

    public List<(Guid BookingId, string Note)> NoteRequests { get; } = [];

    public List<(Guid BookingId, decimal Price)> PriceRequests { get; } = [];

    public List<(Guid BookingId, DateOnly Date)> SlotRequests { get; } = [];

    public List<(Guid BookingId, DateTimeOffset StartAt)> RescheduleRequests { get; } = [];

    public async Task<PagedResponse<BarberBookingResponse>> GetAsync(
        string? scope = null,
        string? cursor = null,
        int? pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        PageRequests.Add(new PageRequest(scope, cursor, pageSize));

        return await Page(scope, cursor, pageSize, cancellationToken);
    }

    public async Task<Guid> CreateAsync(
        CreateBarberBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        CreateRequests.Add(request);

        return await Create(request, cancellationToken);
    }

    public async Task ConfirmAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        ConfirmRequests.Add(bookingId);

        await Confirm(bookingId, cancellationToken);
    }

    public async Task CompleteAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        CompleteRequests.Add(bookingId);

        await Complete(bookingId, cancellationToken);
    }

    public async Task MarkNoShowAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        NoShowRequests.Add(bookingId);

        await MarkNoShow(bookingId, cancellationToken);
    }

    public async Task CancelAsync(
        Guid bookingId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        CancelRequests.Add((bookingId, reason));

        await Cancel(bookingId, reason, cancellationToken);
    }

    public async Task SaveNoteAsync(
        Guid bookingId,
        string note,
        CancellationToken cancellationToken = default)
    {
        NoteRequests.Add((bookingId, note));

        await SaveNote(bookingId, note, cancellationToken);
    }

    public async Task ChangePriceAsync(
        Guid bookingId,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        PriceRequests.Add((bookingId, price));

        await ChangePrice(bookingId, price, cancellationToken);
    }

    public async Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
        Guid bookingId,
        DateOnly slotDate,
        CancellationToken cancellationToken = default)
    {
        SlotRequests.Add((bookingId, slotDate));

        return await RescheduleSlots(bookingId, slotDate, cancellationToken);
    }

    public async Task RescheduleAsync(
        Guid bookingId,
        DateTimeOffset startAt,
        CancellationToken cancellationToken = default)
    {
        RescheduleRequests.Add((bookingId, startAt));

        await Reschedule(bookingId, startAt, cancellationToken);
    }
}

/// <summary>
/// Параметры страницы записей, чтобы проверять, что именно ушло на сервер.
/// </summary>
/// <param name="Scope">Скоуп.</param>
/// <param name="Cursor">Курсор.</param>
/// <param name="PageSize">Размер страницы.</param>
internal sealed record PageRequest(string? Scope, string? Cursor, int? PageSize);

/// <summary>
/// Подменённый клиент формы записи: услуги, салоны, сетка слотов и поиск клиентов.
/// </summary>
internal sealed class FakeBarberClientsApi : IBarberClientsApi
{
    public Func<CancellationToken, Task<BarberBookingOptionsResponse>> BookingOptions { get; set; } =
        static _ => Task.FromResult(BarberCabinetFixtures.Options());

    public Func<DateOnly, Guid?, Guid?, CancellationToken, Task<BarberSlotsResponse>> Slots { get; set; } =
        static (day, _, _, _) => Task.FromResult(BarberCabinetFixtures.Slots(
            day,
            slots: [
                new BarberSlotResponse(
                    new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(3)),
                    new DateTimeOffset(day.ToDateTime(new TimeOnly(11, 0)), TimeSpan.FromHours(3))),
            ]));

    public Func<string, CancellationToken, Task<IReadOnlyList<BarberClientResponse>>> Search { get; set; } =
        static (_, _) => Task.FromResult<IReadOnlyList<BarberClientResponse>>([]);

    public int OptionsCalls { get; private set; }

    public List<SlotRequest> SlotRequests { get; } = [];

    public List<string> SearchQueries { get; } = [];

    public async Task<BarberBookingOptionsResponse> GetBookingOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        OptionsCalls++;

        return await BookingOptions(cancellationToken);
    }

    public async Task<BarberSlotsResponse> GetSlotsAsync(
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId,
        CancellationToken cancellationToken = default)
    {
        SlotRequests.Add(new SlotRequest(slotDate, hairServiceId, salonId));

        return await Slots(slotDate, hairServiceId, salonId, cancellationToken);
    }

    public async Task<IReadOnlyList<BarberClientResponse>> SearchClientsAsync(
        string search,
        CancellationToken cancellationToken = default)
    {
        SearchQueries.Add(search);

        return await Search(search, cancellationToken);
    }
}

/// <summary>
/// Параметры чтения сетки слотов.
/// </summary>
/// <param name="Date">Салонная дата.</param>
/// <param name="HairServiceId">Услуга справочника.</param>
/// <param name="SalonId">Салон.</param>
internal sealed record SlotRequest(DateOnly Date, Guid? HairServiceId, Guid? SalonId);

/// <summary>
/// Подменённый счётчик уведомлений: кабинет читает только его.
/// </summary>
internal sealed class FakeBarberNotificationsApi : IBarberNotificationsApi
{
    public Func<CancellationToken, Task<int>> UnreadCount { get; set; } = static _ => Task.FromResult(0);

    public int UnreadCalls { get; private set; }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        UnreadCalls++;

        return await UnreadCount(cancellationToken);
    }

    public Task<PagedResponse<BarberNotificationResponse>> GetAsync(
        string? cursor = null,
        int? pageSize = 20,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не читает инбокс.");

    public Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не отмечает уведомления.");

    public Task MarkAllReadAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не отмечает уведомления.");

    public Task<NotificationSettingsResponse> GetSettingsAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не читает настройки напоминаний.");

    public Task<NotificationSettingsResponse> UpdateSettingsAsync(
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не меняет настройки напоминаний.");

    public Task<TelegramLinkResponse> CreateTelegramLinkAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не привязывает Telegram.");

    public Task UnlinkTelegramAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не отвязывает Telegram.");
}

/// <summary>
/// Подменённый клиент аккаунта: кабинету нужен только <c>/me</c>.
/// </summary>
internal sealed class FakeAccountApi : IAccountApi
{
    public Func<CancellationToken, Task<MeResponse>> Me { get; set; } =
        static _ => Task.FromResult(BarberCabinetFixtures.Me());

    public int MeCalls { get; private set; }

    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        MeCalls++;

        return await Me(cancellationToken);
    }

    public Task<MyProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не читает профиль аккаунта.");

    public Task UpdateProfileAsync(
        Guid userId,
        string firstName,
        string lastName,
        string? phoneNumber,
        string? avatarUrl,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не правит профиль аккаунта.");

    public Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не меняет пароль.");

    public Task RequestEmailChangeAsync(
        string newEmail,
        string currentPassword,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не меняет email.");

    public Task RequestEmailVerificationAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Кабинет не отправляет письмо подтверждения.");
}

/// <summary>
/// Подменённое хранилище пары токенов.
/// </summary>
internal sealed class FakeTokenStore : ITokenStore
{
    public StoredTokens? Tokens { get; set; }

    public Task<StoredTokens?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Tokens);

    public Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
    {
        Tokens = tokens;

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Tokens = null;

        return Task.CompletedTask;
    }
}
