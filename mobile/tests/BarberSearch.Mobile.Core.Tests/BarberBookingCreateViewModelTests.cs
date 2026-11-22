using System.Net;
using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки формы «Записать клиента»: данные формы, поиск клиента с паузой, режимы
/// «зарегистрированный» и «новый», окно дат в поясе салона, предвыбор слота, тело
/// <c>POST</c>, ключ идемпотентности и защита от двойного нажатия.
/// </summary>
/// <remarks>
/// «Сейчас» — 22:00 UTC 14 ноября, то есть 15 ноября в Москве: календарь формы обязан
/// начинаться с салонного дня, а не с UTC. Пауза поиска клиента приходит делегатом, поэтому
/// debounce проверяется без ожидания; чтения повторяются циклом без настоящих пауз, а запись
/// не повторяется вовсе.
/// </remarks>
public sealed class BarberBookingCreateViewModelTests
{
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string RateLimitText = "Слишком много запросов. Подождите минуту и повторите.";
    private const string OptionsFailedText = "Не удалось загрузить услуги и салоны.";
    private const string OverlapText = "Это время только что заняли. Выберите другое.";
    private const string GuestNameRequiredText = "Укажите имя гостя.";
    private const string GuestPhoneRequiredText = "Укажите телефон гостя.";
    private const string GuestPhoneInvalidText =
        "Похоже, в номере опечатка. Нужен российский мобильный номер, например +7 900 000-00-00.";

    private static readonly Guid CreatedBookingId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000e1");

    [Fact]
    public async Task InitializeAsync_LoadsOptionsAndSelectsPrimarySalon()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Clients.OptionsCalls);
        Assert.Single(viewModel.Services);
        Assert.Single(viewModel.Salons);
        Assert.True(viewModel.SelectedSalon?.IsPrimary);
        Assert.Equal(BarberCabinetFixtures.SalonId, viewModel.SelectedSalon?.SalonId);
        Assert.Null(viewModel.SelectedService);
        Assert.True(viewModel.ShowForm);
        Assert.True(viewModel.IsRegisteredMode);
        Assert.False(viewModel.IsGuestMode);
    }

    [Fact]
    public async Task InitializeAsync_TransportFailure_ShowsOptionsError()
    {
        var harness = new Harness();
        harness.Clients.BookingOptions = _ => Task.FromException<BarberBookingOptionsResponse>(
            new HttpRequestException("offline"));

        await harness.ViewModel.InitializeAsync();

        Assert.Equal(NetworkText, harness.ViewModel.OptionsError);
        Assert.True(harness.ViewModel.ShowOptionsError);
        Assert.False(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.IsLoadingOptions);
    }

    [Fact]
    public async Task InitializeAsync_RetryableFailure_RetriesOptionsUntilLoaded()
    {
        var harness = new Harness(
            optionsRetry: BarberCabinetRetry.Fast());
        var calls = 0;

        harness.Clients.BookingOptions = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberBookingOptionsResponse>(
                    new HttpRequestException("offline"))
                : Task.FromResult(BarberCabinetFixtures.Options());
        };

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.OptionsAutoRetry.Completion;

        Assert.Equal(2, calls);
        Assert.Null(harness.ViewModel.OptionsError);
        Assert.True(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.IsAutoRetryingOptions);
    }

    [Fact]
    public async Task InitializeAsync_TerminalFailure_DoesNotStartOptionsRetry()
    {
        var harness = new Harness(optionsRetry: BarberCabinetRetry.Fast());
        harness.Clients.BookingOptions = _ => Task.FromException<BarberBookingOptionsResponse>(
            BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "barberProfile.notFound"));

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.InitializeAsync();

        Assert.Equal(OptionsFailedText, harness.ViewModel.OptionsError);
        Assert.Equal(0, harness.ViewModel.OptionsAutoRetry.Attempts);
        Assert.False(harness.ViewModel.OptionsAutoRetry.IsRunning);
    }

    [Fact]
    public void ClientQuery_ShorterThanThreeCharacters_MakesNoSearchRequest()
    {
        var harness = new Harness();

        harness.ViewModel.ClientQuery = "iv";

        Assert.False(harness.ViewModel.CanSearchClients);
        Assert.Contains("Минимум 3", harness.ViewModel.ClientSearchHint, StringComparison.Ordinal);
        Assert.False(harness.ViewModel.ShowClientsEmpty);
        Assert.Empty(harness.Clients.SearchQueries);
        Assert.False(harness.ViewModel.IsSearchingClients);
    }

    [Fact]
    public async Task ClientQuery_ThreeCharacters_AfterDebounce_Searches()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(delay: (_, cancellationToken) => gate.Task.WaitAsync(cancellationToken));
        harness.Clients.Search = (_, _) => Task.FromResult<IReadOnlyList<BarberClientResponse>>(
            [BarberCabinetFixtures.Client()]);

        harness.ViewModel.ClientQuery = "iva";

        Assert.True(harness.ViewModel.CanSearchClients);
        Assert.True(harness.ViewModel.IsSearchingClients);
        Assert.Empty(harness.Clients.SearchQueries);

        gate.SetResult();

        await harness.ViewModel.ClientsOperation;

        Assert.Equal(["iva"], harness.Clients.SearchQueries);
        Assert.Single(harness.ViewModel.Clients);
        Assert.True(harness.ViewModel.ShowClientResults);
        Assert.False(harness.ViewModel.IsSearchingClients);
    }

    [Fact]
    public async Task ClientQuery_NewerText_CancelsPreviousDebounce()
    {
        var gates = new List<TaskCompletionSource>();
        var harness = new Harness(delay: (_, cancellationToken) =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            gates.Add(gate);

            return gate.Task.WaitAsync(cancellationToken);
        });
        harness.Clients.Search = (_, _) => Task.FromResult<IReadOnlyList<BarberClientResponse>>(
            [BarberCabinetFixtures.Client()]);

        harness.ViewModel.ClientQuery = "iva";
        harness.ViewModel.ClientQuery = "ivan";

        Assert.Equal(2, gates.Count);

        gates[1].SetResult();

        await harness.ViewModel.ClientsOperation;

        Assert.Equal(["ivan"], harness.Clients.SearchQueries);
        Assert.Single(harness.ViewModel.Clients);
    }

    [Fact]
    public async Task SubmitAsync_GuestWithoutContacts_ShowsFieldErrorsWithoutRequest()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();
        harness.SelectFirstSlot();
        harness.ViewModel.SelectGuestModeCommand.Execute(null);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Empty(harness.Bookings.CreateRequests);
        Assert.Equal(GuestNameRequiredText, harness.ViewModel.GuestNameError);
        Assert.Equal(GuestPhoneRequiredText, harness.ViewModel.GuestPhoneError);
        Assert.True(harness.ViewModel.HasGuestNameError);
        Assert.True(harness.ViewModel.HasGuestPhoneError);
    }

    [Fact]
    public async Task SubmitAsync_Guest_SendsGuestContactsAndTrimmedNote()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();
        harness.SelectFirstSlot();
        harness.Bookings.Create = (_, _) => Task.FromResult(CreatedBookingId);

        var viewModel = harness.ViewModel;

        viewModel.SelectGuestModeCommand.Execute(null);
        viewModel.GuestFirstName = "  Иван  ";
        viewModel.GuestLastName = " Петров ";
        viewModel.GuestPhone = " +7 900 000-00-00 ";
        viewModel.Note = "  Покороче  ";

        await viewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Bookings.CreateRequests);

        Assert.True(viewModel.IsGuestMode);
        Assert.Null(request.CustomerId);
        Assert.Equal("Иван", request.GuestFirstName);
        Assert.Equal("Петров", request.GuestLastName);
        Assert.Equal("+7 900 000-00-00", request.GuestPhone);
        Assert.Equal("Покороче", request.CustomerNote);
        Assert.Equal(BarberCabinetFixtures.BarberServiceId, request.BarberServiceId);
        Assert.Equal(BarberCabinetFixtures.SalonId, request.SalonId);
        Assert.Equal(viewModel.SelectedSlot!.StartAt, request.StartAt);
        Assert.Equal(viewModel.SelectedSlot.EndAt, request.EndAt);
    }

    [Theory]
    [InlineData("9991234567", "+79991234567")]
    [InlineData("8 (999) 123-45-67", "+7 (999) 123-45-67")]
    [InlineData("+7 999 123-45-67", "+7 999 123-45-67")]
    public void GuestPhone_Input_GetsRussianCountryCode(string input, string expected)
    {
        var harness = new Harness();

        harness.ViewModel.GuestPhone = input;

        Assert.Equal(expected, harness.ViewModel.GuestPhone);
    }

    [Fact]
    public void GuestPhone_AfterRejectedFormat_ClearsFieldErrorOnEdit()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.GuestPhone = "999";
        viewModel.GuestPhoneError = GuestPhoneInvalidText;

        viewModel.GuestPhone = "9991";

        Assert.Null(viewModel.GuestPhoneError);
        Assert.False(viewModel.HasGuestPhoneError);
    }

    [Fact]
    public async Task GuestContacts_PhoneWithOnlyPrefix_IsNotFilled()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();
        harness.SelectFirstSlot();

        var viewModel = harness.ViewModel;

        viewModel.SelectGuestModeCommand.Execute(null);
        viewModel.GuestFirstName = "Иван";
        viewModel.GuestPhone = PhoneInput.MobilePrefix;

        Assert.False(viewModel.HasGuestContacts);
        Assert.False(viewModel.HasClient);
        Assert.False(viewModel.CanSubmit);
        Assert.False(viewModel.SubmitCommand.CanExecute(null));

        viewModel.GuestPhone = "+79991234567";

        Assert.True(viewModel.HasGuestContacts);
        Assert.True(viewModel.HasClient);
        Assert.True(viewModel.CanSubmit);
        Assert.True(viewModel.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuestContacts_FillingOrder_NotifiesSubmitState()
    {
        var phoneLast = new Harness();

        await phoneLast.PrepareFormAsync();
        phoneLast.SelectFirstSlot();
        phoneLast.ViewModel.SelectGuestModeCommand.Execute(null);

        var phoneLastNotifications = new List<string?>();

        phoneLast.ViewModel.PropertyChanged += (_, e) => phoneLastNotifications.Add(e.PropertyName);

        phoneLast.ViewModel.GuestFirstName = "Иван";
        phoneLastNotifications.Clear();

        phoneLast.ViewModel.GuestPhone = "+79991234567";

        // Кнопка обязана проснуться сразу, без повторного выбора времени: телефон
        // заполняют последним так же часто, как и первым.
        Assert.True(phoneLast.ViewModel.CanSubmit);
        Assert.True(phoneLast.ViewModel.SubmitCommand.CanExecute(null));
        Assert.Contains(nameof(BarberBookingCreateViewModel.CanSubmit), phoneLastNotifications);
        Assert.Contains(nameof(BarberBookingCreateViewModel.HasClient), phoneLastNotifications);

        var nameLast = new Harness();

        await nameLast.PrepareFormAsync();
        nameLast.SelectFirstSlot();
        nameLast.ViewModel.SelectGuestModeCommand.Execute(null);

        var nameLastNotifications = new List<string?>();

        nameLast.ViewModel.PropertyChanged += (_, e) => nameLastNotifications.Add(e.PropertyName);

        nameLast.ViewModel.GuestPhone = "+79991234567";

        Assert.False(nameLast.ViewModel.CanSubmit);

        nameLastNotifications.Clear();

        nameLast.ViewModel.GuestFirstName = "Иван";

        Assert.True(nameLast.ViewModel.CanSubmit);
        Assert.True(nameLast.ViewModel.SubmitCommand.CanExecute(null));
        Assert.Contains(nameof(BarberBookingCreateViewModel.CanSubmit), nameLastNotifications);
        Assert.Contains(nameof(BarberBookingCreateViewModel.HasClient), nameLastNotifications);
    }

    [Fact]
    public async Task SubmitAsync_GuestPhoneRejected_ShowsFieldErrorWithoutFormText()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();
        harness.SelectFirstSlot();
        harness.Bookings.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "booking.create.guestPhone"));

        var viewModel = harness.ViewModel;

        viewModel.SelectGuestModeCommand.Execute(null);
        viewModel.GuestFirstName = "Иван";
        viewModel.GuestPhone = "999123";

        await viewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Bookings.CreateRequests);

        Assert.Equal("+7999123", request.GuestPhone);
        Assert.Equal(GuestPhoneInvalidText, viewModel.GuestPhoneError);
        Assert.True(viewModel.HasGuestPhoneError);
        Assert.Null(viewModel.SubmitError);
        Assert.False(viewModel.HasSubmitError);
    }

    [Fact]
    public async Task SubmitAsync_Registered_SendsCustomerIdAndNoGuestFields()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();
        harness.SelectFirstClient();
        harness.SelectFirstSlot();
        harness.Bookings.Create = (_, _) => Task.FromResult(CreatedBookingId);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Bookings.CreateRequests);

        Assert.Equal(BarberCabinetFixtures.CustomerId, request.CustomerId);
        Assert.Null(request.GuestFirstName);
        Assert.Null(request.GuestLastName);
        Assert.Null(request.GuestPhone);
    }

    [Fact]
    public async Task SubmitAsync_RegisteredSuccess_RaisesBookingCreatedWithClientName()
    {
        var harness = new Harness();
        BarberBookingCreatedResult? created = null;

        await harness.PrepareFormAsync();
        harness.SelectFirstClient();
        harness.SelectFirstSlot();
        harness.Bookings.Create = (_, _) => Task.FromResult(CreatedBookingId);
        harness.ViewModel.BookingCreated += (_, result) => created = result;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Equal(CreatedBookingId, created.BookingId);
        Assert.Equal("Иван Петров", created.ClientName);
        Assert.False(harness.ViewModel.IsSubmitting);
        Assert.Null(harness.ViewModel.SubmitError);
    }

    [Fact]
    public async Task SubmitAsync_RetryAfterTransportFailure_ReusesIdempotencyKey()
    {
        var harness = new Harness();
        var calls = 0;

        await harness.PrepareFormAsync();
        harness.SelectFirstClient();
        harness.SelectFirstSlot();
        harness.Bookings.Create = (_, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<Guid>(new HttpRequestException("offline"))
                : Task.FromResult(CreatedBookingId);
        };

        var viewModel = harness.ViewModel;
        var key = viewModel.IdempotencyKey;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, viewModel.SubmitError);
        Assert.Equal(key, viewModel.IdempotencyKey);

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Bookings.CreateRequests.Count);
        Assert.All(
            harness.Bookings.CreateRequests,
            request => Assert.Equal(key, request.IdempotencyKey));
    }

    [Fact]
    public async Task NoteChanged_StartsNewIdempotencyKey()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        var key = harness.ViewModel.IdempotencyKey;

        harness.ViewModel.Note = "Покороче";

        Assert.NotEqual(key, harness.ViewModel.IdempotencyKey);
    }

    [Fact]
    public async Task SubmitAsync_Overlap_ReloadsSameDateSlotsAndKeepsForm()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();
        harness.SelectFirstClient();
        harness.ViewModel.SelectedDate = new DateTime(2026, 11, 20);

        await harness.ViewModel.SlotsOperation;

        harness.SelectFirstSlot();
        harness.ViewModel.Note = "Покороче";

        var slotCountBefore = harness.Clients.SlotRequests.Count;

        harness.Bookings.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.Conflict, "booking.add.overlap"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.Equal(OverlapText, viewModel.SubmitError);
        Assert.Equal(slotCountBefore + 1, harness.Clients.SlotRequests.Count);
        Assert.Equal(
            new DateOnly(2026, 11, 20),
            harness.Clients.SlotRequests[^1].Date);
        Assert.Null(viewModel.SelectedSlot);
        Assert.Equal(BarberCabinetFixtures.SalonId, viewModel.SelectedSalon?.SalonId);
        Assert.Equal(BarberCabinetFixtures.BarberServiceId, viewModel.SelectedService?.BarberServiceId);
        Assert.NotNull(viewModel.SelectedClient);
        Assert.Equal("Покороче", viewModel.Note);
        Assert.NotEmpty(viewModel.Slots);
    }

    [Fact]
    public async Task SubmitAsync_DoubleTap_SendsSingleRequest()
    {
        var harness = new Harness();
        var createGate = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;

        await harness.PrepareFormAsync();
        harness.SelectFirstClient();
        harness.SelectFirstSlot();
        harness.Bookings.Create = (_, _) => createGate.Task;
        harness.ViewModel.BookingCreated += (_, _) => created++;

        var first = harness.ViewModel.SubmitCommand.ExecuteAsync(null);
        var second = harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Single(harness.Bookings.CreateRequests);

        createGate.SetResult(CreatedBookingId);

        await Task.WhenAll(first, second);

        Assert.Single(harness.Bookings.CreateRequests);
        Assert.Equal(1, created);
    }

    [Fact]
    public async Task SelectedService_LoadsSlotsForSalonDateAndHairService()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.SelectedService = harness.ViewModel.Services[0];
        await harness.ViewModel.SlotsOperation;

        var request = Assert.Single(harness.Clients.SlotRequests);

        Assert.Equal(new DateOnly(2026, 11, 15), request.Date);
        Assert.Equal(BarberCabinetFixtures.HairServiceId, request.HairServiceId);
        Assert.Equal(BarberCabinetFixtures.SalonId, request.SalonId);
        Assert.Single(harness.ViewModel.Slots);
        Assert.Equal("10:00–11:00", harness.ViewModel.Slots[0].TimeText);
        Assert.True(harness.ViewModel.ShowSlots);
    }

    [Fact]
    public async Task InitializeAsync_PreferredStartAtMatches_SelectsExactSlot()
    {
        var harness = new Harness();
        var preferred = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(3));

        harness.Clients.Slots = (day, _, _, _) => Task.FromResult(
            BarberCabinetFixtures.Slots(
                day,
                slots:
                [
                    new BarberSlotResponse(
                        new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(3)),
                        new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(3))),
                    new BarberSlotResponse(
                        new DateTimeOffset(day.ToDateTime(new TimeOnly(11, 0)), TimeSpan.FromHours(3)),
                        new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(3))),
                ]));

        await harness.ViewModel.InitializeAsync(preferred);
        harness.ViewModel.SelectedService = harness.ViewModel.Services[0];
        await harness.ViewModel.SlotsOperation;

        Assert.NotNull(harness.ViewModel.SelectedSlot);
        Assert.Equal(preferred, harness.ViewModel.SelectedSlot.StartAt);
    }

    [Fact]
    public async Task InitializeAsync_PreferredStartAtDifferentInstant_SelectsNoSlot()
    {
        var harness = new Harness();
        var preferred = new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.FromHours(3));

        await harness.ViewModel.InitializeAsync(preferred);
        harness.ViewModel.SelectedService = harness.ViewModel.Services[0];
        await harness.ViewModel.SlotsOperation;

        Assert.Null(harness.ViewModel.SelectedSlot);
        Assert.DoesNotContain(harness.ViewModel.Slots, slot => slot.IsSelected);
    }

    [Fact]
    public async Task SelectedDate_BeyondHorizon_ClampsToMaximumAndLoadsThatDate()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.SelectedService = harness.ViewModel.Services[0];
        await harness.ViewModel.SlotsOperation;

        harness.ViewModel.SelectedDate = new DateTime(2027, 1, 1);

        await harness.ViewModel.SlotsOperation;

        Assert.Equal(harness.ViewModel.MaximumDate, harness.ViewModel.SelectedDate);
        Assert.Equal(new DateTime(2026, 12, 15), harness.ViewModel.SelectedDate);
        Assert.Equal(
            new DateOnly(2026, 12, 15),
            harness.Clients.SlotRequests[^1].Date);
    }

    [Fact]
    public async Task SlotsTransportFailure_WhileVisible_RetriesUntilLoaded()
    {
        var harness = new Harness(slotsRetry: BarberCabinetRetry.Fast());
        var calls = 0;

        harness.Clients.Slots = (day, _, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberSlotsResponse>(
                    new HttpRequestException("offline"))
                : Task.FromResult(BarberCabinetFixtures.Slots(
                    day,
                    slots:
                    [
                        new BarberSlotResponse(
                            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(3)),
                            new DateTimeOffset(day.ToDateTime(new TimeOnly(11, 0)), TimeSpan.FromHours(3))),
                    ]));
        };

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.SelectedService = harness.ViewModel.Services[0];
        await harness.ViewModel.SlotsOperation;
        await harness.ViewModel.SlotsAutoRetry.Completion;

        Assert.Equal(2, calls);
        Assert.Single(harness.ViewModel.Slots);
        Assert.Null(harness.ViewModel.SlotsError);
        Assert.False(harness.ViewModel.IsAutoRetryingSlots);
    }

    [Fact]
    public async Task ClientsTransportFailure_WhileVisible_RetriesUntilLoaded()
    {
        var harness = new Harness(clientsRetry: BarberCabinetRetry.Fast());
        var calls = 0;

        harness.Clients.Search = (_, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<IReadOnlyList<BarberClientResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult<IReadOnlyList<BarberClientResponse>>(
                    [BarberCabinetFixtures.Client()]);
        };

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.ClientQuery = "ivan";
        await harness.ViewModel.ClientsOperation;
        await harness.ViewModel.ClientsAutoRetry.Completion;

        Assert.Equal(2, calls);
        Assert.Single(harness.ViewModel.Clients);
        Assert.Null(harness.ViewModel.ClientsError);
        Assert.False(harness.ViewModel.IsAutoRetryingClients);
    }

    [Fact]
    public async Task SubmitAsync_RateLimited_ShowsRateLimitTextAndDoesNotRetryWrite()
    {
        var harness = new Harness(
            optionsRetry: BarberCabinetRetry.Fast(),
            slotsRetry: BarberCabinetRetry.Fast(),
            clientsRetry: BarberCabinetRetry.Fast());

        harness.ViewModel.StartAutoRetry();
        await harness.PrepareFormAsync();
        harness.SelectFirstClient();
        harness.SelectFirstSlot();
        harness.Bookings.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.TooManyRequests, "rate.limited"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        // 429 относится к повторяемым отказам чтения, но отправка — запись: цикл не стартует.
        Assert.Equal(RateLimitText, harness.ViewModel.SubmitError);
        Assert.Single(harness.Bookings.CreateRequests);
        Assert.Equal(0, harness.ViewModel.OptionsAutoRetry.Attempts);
        Assert.Equal(0, harness.ViewModel.SlotsAutoRetry.Attempts);
        Assert.Equal(0, harness.ViewModel.ClientsAutoRetry.Attempts);
        Assert.True(harness.ViewModel.AutoRetryCompletion.IsCompleted);
    }

    /// <summary>
    /// Собранная форма: клиенты, записи, подменяемая пауза поиска и часы.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            Func<TimeSpan, CancellationToken, Task>? delay = null,
            AutoRetryLoop? optionsRetry = null,
            AutoRetryLoop? slotsRetry = null,
            AutoRetryLoop? clientsRetry = null) =>
            ViewModel = new BarberBookingCreateViewModel(
                Clients,
                Bookings,
                new FixedTimeProvider(BarberCabinetFixtures.Now),
                delay ?? (static (_, _) => Task.CompletedTask),
                optionsRetry,
                slotsRetry,
                clientsRetry);

        public FakeBarberClientsApi Clients { get; } = new();

        public FakeBarberBookingsApi Bookings { get; } = new();

        public BarberBookingCreateViewModel ViewModel { get; }

        /// <summary>
        /// Загружает форму и первую сетку слотов на салонный «сегодня».
        /// </summary>
        /// <returns>Задача подготовки.</returns>
        public async Task PrepareFormAsync()
        {
            await ViewModel.InitializeAsync();

            ViewModel.SelectedService = ViewModel.Services[0];

            await ViewModel.SlotsOperation;
        }

        /// <summary>
        /// Подставляет первого найденного клиента как выбранного.
        /// </summary>
        public void SelectFirstClient()
        {
            ViewModel.Clients.Add(new BarberClientRow(BarberCabinetFixtures.Client()));
            ViewModel.SelectedClient = ViewModel.Clients[0];
        }

        /// <summary>
        /// Выбирает первый слот сетки.
        /// </summary>
        public void SelectFirstSlot() => ViewModel.SelectSlotCommand.Execute(ViewModel.Slots[0]);
    }
}
