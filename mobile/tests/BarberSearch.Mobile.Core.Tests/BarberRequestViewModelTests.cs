using System.Net;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.BarberRequests;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Стать мастером»: чтение своей заявки, форма, отправка и статус.
/// </summary>
/// <remarks>
/// <para>
/// Тексты сверяются дословно с таблицей веба: валидация и отказы — часть одного продукта
/// в двух клиентах, и расхождение здесь читается человеком как «две разные формы».
/// </para>
/// <para>
/// Сеть подменяется клиентом <see cref="IBarberRequestsApi"/>: тест считает вызовы, поэтому
/// доказывает не только «что показано», но и «чего не было» — второго чтения после
/// <c>201</c>, повторного <c>POST</c> по второму нажатию, запроса при ошибке валидации.
/// Автоповтор проверяется подменённым <see cref="AutoRetryLoop"/> без настоящих пауз.
/// </para>
/// <para>
/// Даты считаются от <see cref="FixedTimeProvider"/>: «подана» и «рассмотрена» зависят от
/// текущего года, и тест не должен менять результат 31 декабря.
/// </para>
/// </remarks>
public sealed class BarberRequestViewModelTests
{
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string ReadFailedText = "Не удалось проверить заявку.";
    private const string TooManyRequestsText = "Слишком много запросов. Подождите минуту и повторите.";
    private const string SubmitFailedText = "Не удалось отправить заявку. Попробуйте ещё раз.";
    private const string OnlyCustomerText = "Заявку может подать только клиент.";
    private const string AlreadyPendingText =
        "Заявка уже на рассмотрении. Дождитесь решения администратора — статус виден в кабинете.";
    private const string NameRequiredText = "Введите имя, которое увидят клиенты.";
    private const string NameTooLongText = "Имя — не длиннее 120 символов.";
    private const string ExperienceRequiredText = "Укажите стаж в годах.";
    private const string ExperienceRangeText = "Стаж — целое число от 1 года до 70 лет.";
    private const string CityRequiredText = "Укажите город.";
    private const string CityTooLongText = "Город — не длиннее 100 символов.";
    private const string AboutTooLongText = "О себе — не длиннее 2000 символов.";
    private const string RejectionReasonMissingText = "Причина не указана.";
    private const string PendingHeading = "Заявка на рассмотрении";
    private const string PendingLabel = "На рассмотрении";
    private const string RejectedHeading = "Заявка отклонена";
    private const string ApprovedHeading = "Заявка одобрена";
    private const string JustCreatedHeading = "Заявка отправлена";
    private const string SubmitButtonIdle = "Отправить заявку";
    private const string SubmitButtonBusy = "Отправляем…";

    /// <summary>
    /// «Сейчас» экрана: год служебных моментов проверяется без ожидания настоящих часов.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 1, 12, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ReviewedAt = new(2026, 11, 5, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeAsync_NoRequest_ShowsForm()
    {
        using var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.ShowLoading);
        Assert.False(harness.ViewModel.ShowStatus);
        Assert.False(harness.ViewModel.ShowError);
        Assert.False(harness.ViewModel.ShowSessionExpired);
        Assert.Null(harness.ViewModel.Request);
        Assert.Equal(1, harness.Api.MineCalls);
    }

    [Fact]
    public async Task InitializeAsync_PendingRequest_ShowsStatusWithPendingHeading()
    {
        using var harness = await CreateLoadedAsync(Request());

        Assert.True(harness.ViewModel.ShowStatus);
        Assert.False(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.IsJustCreated);
        Assert.Equal(PendingHeading, harness.ViewModel.StatusHeading);
        Assert.Equal(PendingLabel, harness.ViewModel.StatusLabel);
        Assert.False(harness.ViewModel.IsApproved);
        Assert.False(harness.ViewModel.CanReapply);
        Assert.NotNull(harness.ViewModel.Request);
        Assert.Equal(BarberRequestStatus.Pending, harness.ViewModel.Request.Status);
    }

    [Fact]
    public async Task InitializeAsync_RejectedRequest_ShowsReasonAndReapply()
    {
        using var harness = await CreateLoadedAsync(
            Request(status: BarberRequestStatus.Rejected, rejectionReason: "Не хватает портфолио."));

        Assert.True(harness.ViewModel.ShowStatus);
        Assert.Equal(RejectedHeading, harness.ViewModel.StatusHeading);
        Assert.True(harness.ViewModel.CanReapply);
        Assert.False(harness.ViewModel.IsApproved);
        Assert.Equal("Не хватает портфолио.", harness.ViewModel.RejectionReasonText);
    }

    [Fact]
    public async Task InitializeAsync_RejectedRequestWithoutReason_ShowsMissingReason()
    {
        using var harness = await CreateLoadedAsync(Request(status: BarberRequestStatus.Rejected));

        // Отказ без причины — не пустая строка: человек должен видеть, что причина не указана,
        // а не гадать, потерялся ли текст.
        Assert.Equal(RejectionReasonMissingText, harness.ViewModel.RejectionReasonText);
        Assert.True(harness.ViewModel.CanReapply);
    }

    [Fact]
    public async Task InitializeAsync_ApprovedRequest_ShowsApprovedStatus()
    {
        using var harness = await CreateLoadedAsync(
            Request(status: BarberRequestStatus.Approved, reviewedAt: ReviewedAt));

        Assert.True(harness.ViewModel.ShowStatus);
        Assert.Equal(ApprovedHeading, harness.ViewModel.StatusHeading);
        Assert.Equal("Одобрена", harness.ViewModel.StatusLabel);
        Assert.True(harness.ViewModel.IsApproved);
        Assert.False(harness.ViewModel.CanReapply);
    }

    [Fact]
    public async Task InitializeAsync_RequestPresent_FillsDetailTexts()
    {
        using var harness = new Harness(now: Now);

        harness.Api.Mine = () => Task.FromResult(new MyBarberRequestResponse(Request(
            about: "Стригу с 2019 года.",
            hasSalon: true)));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal("Ева Мастерова", viewModel.DisplayNameText);
        Assert.Equal("5 лет", viewModel.ExperienceText);
        Assert.Equal("Екатеринбург", viewModel.CityText);
        Assert.Equal("Да, уже в салоне", viewModel.SalonText);
        Assert.Equal("Стригу с 2019 года.", viewModel.AboutText);
        Assert.Equal(
            AccountText.Moment(CreatedAt, TimeZoneInfo.Local, Now),
            viewModel.SubmittedAtText);

        // Решение ещё не принято: строка «Рассмотрена» просто не рисуется.
        Assert.Empty(viewModel.ReviewedAtText);
    }

    [Fact]
    public async Task InitializeAsync_ReviewedRequest_FillsReviewedAtWithDeviceZone()
    {
        using var harness = new Harness(now: Now);

        harness.Api.Mine = () => Task.FromResult(new MyBarberRequestResponse(Request(
            status: BarberRequestStatus.Approved,
            reviewedAt: ReviewedAt)));

        await harness.ViewModel.InitializeAsync();

        Assert.Equal(
            AccountText.Moment(ReviewedAt, TimeZoneInfo.Local, Now),
            harness.ViewModel.ReviewedAtText);

        // Момент считается тем же форматтером, что на других экранах: пояс устройства и
        // правило «год только не текущий» не дублируются в странице.
        Assert.NotEmpty(harness.ViewModel.ReviewedAtText);
    }

    [Theory]
    [InlineData(true, "Да, уже в салоне")]
    [InlineData(false, "Нет, работает сам")]
    public async Task InitializeAsync_SalonFlag_ShowsSalonWording(bool hasSalon, string expected)
    {
        using var harness = await CreateLoadedAsync(Request(hasSalon: hasSalon));

        Assert.Equal(expected, harness.ViewModel.SalonText);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsReadErrorAndRetry()
    {
        using var harness = new Harness();

        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.False(harness.ViewModel.ShowForm);
        Assert.Equal(NetworkText, harness.ViewModel.ReadError);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task InitializeAsync_ServerFailure_ShowsReadFailedWithoutNetworkWording()
    {
        using var harness = new Harness();

        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();

        // Ответ сервера был: это не отсутствие связи, и обещать проверку подключения нельзя.
        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(ReadFailedText, harness.ViewModel.ReadError);
    }

    [Fact]
    public async Task InitializeAsync_TooManyRequests_ShowsRateLimitReadError()
    {
        using var harness = new Harness();

        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(TooManyRequestsText, harness.ViewModel.ReadError);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ShowsSessionExpired()
    {
        using var harness = new Harness();

        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        // Пара мертва, и обработчик уже пробовал её продлить: второй раз читать нечем.
        Assert.True(harness.ViewModel.ShowSessionExpired);
        Assert.False(harness.ViewModel.ShowError);
        Assert.Empty(harness.ViewModel.ReadError);
    }

    [Fact]
    public async Task RetryReadCommand_AfterFailure_ReadsAgainAndShowsForm()
    {
        using var harness = new Harness();

        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.Api.Mine = () => Task.FromResult(new MyBarberRequestResponse(null));

        // «Повторить» — это «прочитать сейчас», а не «подождать следующую паузу цикла».
        await harness.ViewModel.RetryReadCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.ShowError);
        Assert.Empty(harness.ViewModel.ReadError);
        Assert.Equal(2, harness.Api.MineCalls);
    }

    [Fact]
    public async Task StartAutoRetry_NetworkFailure_RetriesUntilSuccess()
    {
        using var harness = new Harness(FastRetry());
        var calls = 0;

        harness.Api.Mine = () =>
        {
            calls++;

            return calls <= 2
                ? Task.FromException<MyBarberRequestResponse>(new HttpRequestException("refused"))
                : Task.FromResult(new MyBarberRequestResponse(null));
        };

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(2, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(3, harness.Api.MineCalls);
        Assert.True(harness.ViewModel.ShowForm);
        Assert.Empty(harness.ViewModel.ReadError);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart()
    {
        using var harness = new Harness(FastRetry());

        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        // Окончательный отказ не лечится повтором: цикл не должен долбить сервер.
        Assert.False(harness.ViewModel.AutoRetry.IsRunning);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.AutoRetryCompletion.IsCompleted);
        Assert.Equal(1, harness.Api.MineCalls);
    }

    [Fact]
    public async Task StopAutoRetry_DuringWait_StopsFurtherAttempts()
    {
        using var harness = new Harness(StopAfterFirstAttemptRetry());

        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.IsAutoRetrying);

        // Страница ушла в фон: следующий повтор не должен сходить в сеть под чужим экраном.
        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(2, harness.Api.MineCalls);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task SubmitAsync_EmptyForm_ShowsAllFieldErrorsWithoutRequest()
    {
        using var harness = await CreateFormAsync();

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.Equal(NameRequiredText, viewModel.DisplayNameError);
        Assert.Equal(ExperienceRequiredText, viewModel.ExperienceYearsError);
        Assert.Equal(CityRequiredText, viewModel.CityError);

        // «О себе» необязательно: пустое поле не должно считаться ошибкой.
        Assert.False(viewModel.HasAboutError);
        Assert.Null(viewModel.SubmitError);
        Assert.False(viewModel.IsSubmitting);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_EmptyDisplayName_ShowsRequiredError()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);
        harness.ViewModel.DisplayName = "   ";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasDisplayNameError);
        Assert.Equal(NameRequiredText, harness.ViewModel.DisplayNameError);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_TooLongDisplayName_ShowsLimitError()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);
        harness.ViewModel.DisplayName = new string('а', 121);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasDisplayNameError);
        Assert.Equal(NameTooLongText, harness.ViewModel.DisplayNameError);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_EmptyExperience_ShowsRequiredError()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);
        harness.ViewModel.ExperienceYears = string.Empty;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasExperienceYearsError);
        Assert.Equal(ExperienceRequiredText, harness.ViewModel.ExperienceYearsError);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("71")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public async Task SubmitAsync_InvalidExperience_ShowsRangeError(string value)
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);
        harness.ViewModel.ExperienceYears = value;

        // Стаж — строка: Entry не должен молча округлять «1.5» до двух лет.
        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasExperienceYearsError);
        Assert.Equal(ExperienceRangeText, harness.ViewModel.ExperienceYearsError);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_ExperienceWrittenAsDecimal_SendsWholeYears()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);

        // Веб считает «5.0» целым стажем; расхождение с ним было бы отказом на верном значении.
        harness.ViewModel.ExperienceYears = "5.0";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.HasExperienceYearsError);
        Assert.Equal(1, harness.Api.CreateCalls);
        Assert.Equal(5, harness.Api.CreateRequests[0].YearsOfExperience);
    }

    [Fact]
    public async Task SubmitAsync_EmptyCity_ShowsRequiredError()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);
        harness.ViewModel.City = "  ";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasCityError);
        Assert.Equal(CityRequiredText, harness.ViewModel.CityError);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_TooLongCity_ShowsLimitError()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);
        harness.ViewModel.City = new string('г', 101);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasCityError);
        Assert.Equal(CityTooLongText, harness.ViewModel.CityError);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_TooLongAbout_ShowsLimitError()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);
        harness.ViewModel.About = new string('о', 2001);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasAboutError);
        Assert.Equal(AboutTooLongText, harness.ViewModel.AboutError);
        Assert.Equal(0, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_MaximumAllowedValues_SendsRequest()
    {
        using var harness = await CreateFormAsync();

        harness.ViewModel.DisplayName = new string('а', 120);
        harness.ViewModel.ExperienceYears = "70";
        harness.ViewModel.City = new string('г', 100);
        harness.ViewModel.About = new string('о', 2000);
        harness.ViewModel.HasSalon = true;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        // Границы включительные: 120/70/100/2000 сервер принимает, отсекать их на клиенте
        // значило бы запретить валидную заявку.
        Assert.Equal(1, harness.Api.CreateCalls);

        var request = Assert.Single(harness.Api.CreateRequests);

        Assert.Equal(120, request.DisplayName.Length);
        Assert.Equal(70, request.YearsOfExperience);
        Assert.Equal(100, request.City.Length);
        Assert.Equal(2000, request.About!.Length);
        Assert.True(request.HasSalon);
        Assert.True(harness.ViewModel.ShowStatus);
    }

    [Fact]
    public async Task SubmitAsync_WhileRequestInFlight_ShowsBusyButtonAndIgnoresSecondPress()
    {
        using var harness = await CreateFormAsync();

        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource<BarberRequestResponse>();

        harness.Api.Create = _ =>
        {
            started.TrySetResult();

            return gate.Task;
        };

        FillValidForm(harness.ViewModel);

        var first = harness.ViewModel.SubmitCommand.ExecuteAsync(null);
        await started.Task;

        Assert.True(harness.ViewModel.IsSubmitting);
        Assert.Equal(SubmitButtonBusy, harness.ViewModel.SubmitButtonText);

        // Второе нажатие по заблокированной кнопке не должно создать вторую заявку: запись
        // не повторяется сама, а лишний POST — это чужая заявка на сервере.
        harness.ViewModel.SubmitCommand.Execute(null);

        Assert.Equal(1, harness.Api.CreateCalls);

        gate.SetResult(Request());
        await first;

        Assert.False(harness.ViewModel.IsSubmitting);
        Assert.Equal(SubmitButtonIdle, harness.ViewModel.SubmitButtonText);
        Assert.Equal(1, harness.Api.CreateCalls);
        Assert.True(harness.ViewModel.ShowStatus);
    }

    [Fact]
    public async Task SubmitAsync_Created_ShowsJustCreatedStatusWithoutSecondRead()
    {
        using var harness = await CreateFormAsync();

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowStatus);
        Assert.True(viewModel.IsJustCreated);
        Assert.False(viewModel.ShowForm);
        Assert.Equal(JustCreatedHeading, viewModel.StatusHeading);
        Assert.Equal(PendingLabel, viewModel.StatusLabel);
        Assert.NotEmpty(viewModel.SubmittedAtText);

        // 201 уже содержит заявку: второе чтение только гоняло бы сеть впустую.
        Assert.Equal(1, harness.Api.MineCalls);
        Assert.Equal(1, harness.Api.CreateCalls);
    }

    [Fact]
    public async Task SubmitAsync_WhitespaceAbout_SendsNullAboutAndTrimmedFields()
    {
        using var harness = await CreateFormAsync();

        harness.ViewModel.DisplayName = "  Ева Мастерова  ";
        harness.ViewModel.ExperienceYears = " 7 ";
        harness.ViewModel.City = "  Пермь  ";
        harness.ViewModel.About = "   ";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Api.CreateRequests);

        Assert.Equal("Ева Мастерова", request.DisplayName);
        Assert.Equal(7, request.YearsOfExperience);
        Assert.Equal("Пермь", request.City);

        // Пробельный рассказ — тот же пустой: пустая строка сохранилась бы как текст о себе.
        Assert.Null(request.About);
    }

    [Fact]
    public async Task SubmitAsync_Conflict_ReloadsMineAndShowsPending()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.Conflict, "barberRequest.create.alreadyPending"));
        harness.Api.Mine = () => Task.FromResult(new MyBarberRequestResponse(Request()));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        // 409 — не ошибка ввода: заявка уже есть, и человеку нужен её фактический статус.
        Assert.True(viewModel.ShowStatus);
        Assert.False(viewModel.ShowForm);
        Assert.Null(viewModel.SubmitError);
        Assert.False(viewModel.CanRetrySubmit);
        Assert.False(viewModel.IsJustCreated);
        Assert.Equal(PendingHeading, viewModel.StatusHeading);
        Assert.Equal(2, harness.Api.MineCalls);
    }

    [Fact]
    public async Task SubmitAsync_ConflictAndReloadFails_ShowsAlreadyPendingError()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.Conflict, "barberRequest.create.alreadyPending"));
        harness.Api.Mine = () => Task.FromException<MyBarberRequestResponse>(
            new HttpRequestException("refused"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        // Перечитать не удалось: «Повторить» здесь бессмысленно — повтор упрётся в тот же 409.
        Assert.Equal(AlreadyPendingText, viewModel.SubmitError);
        Assert.True(viewModel.HasSubmitError);
        Assert.False(viewModel.CanRetrySubmit);
        Assert.True(viewModel.ShowForm);
        Assert.Equal(2, harness.Api.MineCalls);
    }

    [Fact]
    public async Task SubmitAsync_Forbidden_ShowsOnlyCustomerError()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.Forbidden, "barberRequest.create.forbidden"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(OnlyCustomerText, harness.ViewModel.SubmitError);
        Assert.True(harness.ViewModel.HasSubmitError);

        // Отказ по роли не лечится повтором: кнопку «Повторить» показывать нечего.
        Assert.False(harness.ViewModel.CanRetrySubmit);
        Assert.True(harness.ViewModel.ShowForm);
    }

    [Fact]
    public async Task SubmitAsync_RateLimited_ShowsRetryableError()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(TooManyRequestsText, harness.ViewModel.SubmitError);
        Assert.True(harness.ViewModel.CanRetrySubmit);
    }

    [Fact]
    public async Task SubmitAsync_NetworkFailure_ShowsRetryableNetworkError()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            new HttpRequestException("refused"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, harness.ViewModel.SubmitError);
        Assert.True(harness.ViewModel.CanRetrySubmit);
        Assert.False(harness.ViewModel.IsSubmitting);
    }

    [Fact]
    public async Task SubmitAsync_Timeout_ShowsRetryableNetworkError()
    {
        using var harness = await CreateFormAsync();

        // HttpClient сообщает об истечении таймаута отменой, а не HttpRequestException.
        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            new TaskCanceledException("Timed out"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, harness.ViewModel.SubmitError);
        Assert.True(harness.ViewModel.CanRetrySubmit);
    }

    [Fact]
    public async Task SubmitAsync_ServerError_ShowsRetryableSubmitError()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(SubmitFailedText, harness.ViewModel.SubmitError);
        Assert.True(harness.ViewModel.CanRetrySubmit);
    }

    [Fact]
    public async Task SubmitAsync_Unauthorized_ShowsSessionExpired()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        // Пара уже мертва: продолжать на этом экране нечем, нужна форма входа.
        Assert.True(harness.ViewModel.ShowSessionExpired);
        Assert.False(harness.ViewModel.ShowForm);
        Assert.Null(harness.ViewModel.SubmitError);
        Assert.False(harness.ViewModel.IsSubmitting);
    }

    [Theory]
    [InlineData("barberProfile.displayName", NameRequiredText)]
    [InlineData("barberProfile.displayName.tooLong", NameTooLongText)]
    [InlineData("barberProfile.yearsOfExperience", ExperienceRangeText)]
    [InlineData("barberRequest.city", CityRequiredText)]
    [InlineData("barberRequest.city.tooLong", CityTooLongText)]
    [InlineData("barberRequest.about.tooLong", AboutTooLongText)]
    public async Task SubmitAsync_ValidationProblemWithKnownCode_ShowsFieldError(string code, string expected)
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.BadRequest, code));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        // Код относится к известному полю: текст берётся из веба и встаёт под поле, а общей
        // строки «не удалось отправить» при этом нет — сервер сказал, что именно не так.
        var (hasError, text) = code switch
        {
            "barberProfile.displayName" or "barberProfile.displayName.tooLong" =>
                (harness.ViewModel.HasDisplayNameError, harness.ViewModel.DisplayNameError),
            "barberProfile.yearsOfExperience" =>
                (harness.ViewModel.HasExperienceYearsError, harness.ViewModel.ExperienceYearsError),
            "barberRequest.city" or "barberRequest.city.tooLong" =>
                (harness.ViewModel.HasCityError, harness.ViewModel.CityError),
            _ => (harness.ViewModel.HasAboutError, harness.ViewModel.AboutError),
        };

        Assert.True(hasError);
        Assert.Equal(expected, text);
        Assert.False(harness.ViewModel.HasSubmitError);
        Assert.False(harness.ViewModel.CanRetrySubmit);
    }

    [Fact]
    public async Task SubmitAsync_ValidationProblemWithUnknownCode_ShowsGeneralError()
    {
        using var harness = await CreateFormAsync();

        harness.Api.Create = _ => Task.FromException<BarberRequestResponse>(
            ApiError(HttpStatusCode.BadRequest, "barberRequest.unknown"));

        FillValidForm(harness.ViewModel);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        // Неизвестный код нельзя повесить на поле: показывается общая строка с повтором.
        Assert.Equal(SubmitFailedText, harness.ViewModel.SubmitError);
        Assert.True(harness.ViewModel.CanRetrySubmit);
        Assert.False(harness.ViewModel.HasDisplayNameError);
        Assert.False(harness.ViewModel.HasCityError);
    }

    [Fact]
    public async Task ReapplyCommand_AfterRejection_OpensEmptyForm()
    {
        using var harness = await CreateLoadedAsync(
            Request(status: BarberRequestStatus.Rejected, rejectionReason: "Не хватает портфолио."));

        harness.ViewModel.DisplayName = "Прошлое имя";
        harness.ViewModel.ExperienceYears = "9";
        harness.ViewModel.City = "Прошлый город";
        harness.ViewModel.About = "Прошлый рассказ";
        harness.ViewModel.HasSalon = true;

        // Веб не предзаполняет повторную заявку: прошлый текст мог быть причиной отказа,
        // и отправлять его по инерции нельзя.
        harness.ViewModel.ReapplyCommand.Execute(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowForm);
        Assert.False(viewModel.ShowStatus);
        Assert.Empty(viewModel.DisplayName);
        Assert.Empty(viewModel.ExperienceYears);
        Assert.Empty(viewModel.City);
        Assert.Empty(viewModel.About);
        Assert.False(viewModel.HasSalon);
        Assert.False(viewModel.HasDisplayNameError);
        Assert.False(viewModel.HasCityError);
        Assert.Null(viewModel.SubmitError);
    }

    [Fact]
    public async Task SignOutAndLeaveCommand_AfterApproval_ClearsSessionAndRaisesLeave()
    {
        using var harness = await CreateLoadedAsync(
            Request(status: BarberRequestStatus.Approved, reviewedAt: ReviewedAt));

        await harness.Session.EnsureLoadedAsync();

        var raised = 0;
        harness.ViewModel.LeaveRequested += (_, _) => raised++;

        Assert.True(harness.Session.IsAuthenticated);

        // Смена роли ротирует SecurityStamp: честный выход нужен и при живом токене,
        // и при мёртвом — новый вход приносит новую роль.
        await harness.ViewModel.SignOutAndLeaveCommand.ExecuteAsync(null);

        Assert.Null(harness.Store.Tokens);
        Assert.False(harness.Session.IsAuthenticated);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void LeaveCommand_RaisesLeaveRequested()
    {
        using var harness = new Harness();
        var raised = 0;

        harness.ViewModel.LeaveRequested += (_, _) => raised++;

        harness.ViewModel.LeaveCommand.Execute(null);

        // «К входу» — тот же уход со страницы, разное только действие до него.
        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Создаёт открытый экран: чтение вернуло заданную заявку (либо ничего).
    /// </summary>
    /// <param name="request">Заявка из ответа; <c>null</c> — заявки нет.</param>
    private static async Task<Harness> CreateLoadedAsync(BarberRequestResponse? request)
    {
        var harness = new Harness();
        harness.Api.Mine = () => Task.FromResult(new MyBarberRequestResponse(request));

        await harness.ViewModel.InitializeAsync();

        return harness;
    }

    /// <summary>
    /// Создаёт открытый экран в состоянии формы: заявки нет, все поля пусты.
    /// </summary>
    private static async Task<Harness> CreateFormAsync()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        return harness;
    }

    /// <summary>
    /// Заполняет форму валидными значениями: тест, проверяющий одно поле, не должен
    /// спотыкаться об ошибки соседних.
    /// </summary>
    /// <param name="viewModel">Экран заявки.</param>
    private static void FillValidForm(BarberRequestViewModel viewModel)
    {
        viewModel.DisplayName = "Ева Мастерова";
        viewModel.ExperienceYears = "5";
        viewModel.City = "Екатеринбург";
        viewModel.About = "Стригу с 2019 года.";
    }

    /// <summary>
    /// Заявка из ответа сервера с полями по умолчанию.
    /// </summary>
    /// <param name="status">Статус заявки.</param>
    /// <param name="displayName">Имя.</param>
    /// <param name="yearsOfExperience">Стаж.</param>
    /// <param name="city">Город.</param>
    /// <param name="about">Рассказ о себе.</param>
    /// <param name="hasSalon">Уже работает в салоне.</param>
    /// <param name="reviewedAt">Когда рассмотрена.</param>
    /// <param name="rejectionReason">Причина отказа.</param>
    private static BarberRequestResponse Request(
        BarberRequestStatus status = BarberRequestStatus.Pending,
        string displayName = "Ева Мастерова",
        int yearsOfExperience = 5,
        string city = "Екатеринбург",
        string? about = null,
        bool hasSalon = false,
        DateTimeOffset? reviewedAt = null,
        string? rejectionReason = null) =>
        new(
            Guid.Parse("0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f70"),
            status,
            displayName,
            yearsOfExperience,
            city,
            about,
            hasSalon,
            CreatedAt,
            reviewedAt,
            rejectionReason);

    /// <summary>
    /// Исключение API, собранное так же, как его собирает разбор ответа сервера.
    /// </summary>
    /// <param name="statusCode">Код состояния.</param>
    /// <param name="codes">Публичные коды ошибок.</param>
    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, params string[] codes) =>
        new(
            statusCode,
            "Request failed.",
            codes,
            new Dictionary<string, string[]>());

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Цикл, у которого первая пауза проходит сразу, а вторая висит до остановки: так видно
    /// состояние между попытками, не ожидая реального времени.
    /// </summary>
    private static AutoRetryLoop StopAfterFirstAttemptRetry()
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
    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
    }

    /// <summary>
    /// Собранный экран: сессия с парой, подменённый клиент заявки и часы.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public Harness(AutoRetryLoop? autoRetry = null, DateTimeOffset? now = null)
        {
            Store.Tokens = new StoredTokens("access-1", "refresh-1");
            Session = new TokenSession(Store);
            Time = new FixedTimeProvider(now ?? Now);
            ViewModel = new BarberRequestViewModel(Api, Session, Time, autoRetry);
        }

        public FakeTokenStore Store { get; } = new();

        public TokenSession Session { get; }

        public FixedTimeProvider Time { get; }

        public FakeBarberRequestsApi Api { get; } = new();

        public BarberRequestViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Session.Dispose();
        }
    }

    /// <summary>
    /// «Сейчас» экрана с возможностью сдвинуть время: год служебных моментов проверяется
    /// без ожидания настоящих часов.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Подменённый клиент заявки: считает чтения и создания, запоминает тела запросов.
    /// </summary>
    private sealed class FakeBarberRequestsApi : IBarberRequestsApi
    {
        public int MineCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public Func<Task<MyBarberRequestResponse>> Mine { get; set; } =
            () => Task.FromResult(new MyBarberRequestResponse(null));

        public Func<CreateBarberRequestRequest, Task<BarberRequestResponse>> Create { get; set; } =
            _ => Task.FromResult(Request());

        public List<CreateBarberRequestRequest> CreateRequests { get; } = [];

        public Task<MyBarberRequestResponse> GetMineAsync(CancellationToken cancellationToken = default)
        {
            MineCalls++;

            return Mine();
        }

        public Task<BarberRequestResponse> CreateAsync(
            CreateBarberRequestRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            CreateRequests.Add(request);

            return Create(request);
        }
    }

    /// <summary>
    /// Подменённое хранилище пары: по нему видно, что выход действительно стёр сессию.
    /// </summary>
    private sealed class FakeTokenStore : ITokenStore
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
}
