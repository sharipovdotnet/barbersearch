using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Профиль» кабинета: личные данные, их сохранение и заявка на смену email.
/// </summary>
/// <remarks>
/// Тексты ошибок сверяются дословно с вебом: экран кабинета — перенос
/// <c>profile-form.tsx</c> и <c>email-card.tsx</c>, и разные формулировки в двух клиентах
/// означали бы две копии одного продукта. Смена email — заявка: успех не меняет показанный
/// адрес, поэтому здесь проверяется и то, что он остался прежним.
/// </remarks>
public sealed class AccountProfileViewModelTests
{
    private const string AvatarUrl = "https://cdn.example.test/avatar.jpg";
    private const string UserEmail = "mobile-test@barbersearch.test";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    /// <summary>
    /// «Сейчас» экрана: год служебных моментов проверяется без ожидания реальных часов.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeAsync_WithSession_FillsFormFromProfile()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.Equal("Ева", harness.ViewModel.FirstName);
        Assert.Equal("Клиентова", harness.ViewModel.LastName);
        Assert.Equal("+79000000003", harness.ViewModel.PhoneNumber);
        Assert.Equal(UserEmail, harness.ViewModel.Email);
        Assert.Equal("Клиент", harness.ViewModel.RoleText);
        Assert.NotEmpty(harness.ViewModel.CreatedAtText);
        Assert.DoesNotContain("2026", harness.ViewModel.CreatedAtText);
        Assert.Equal("Ещё не было", harness.ViewModel.LastLoginText);

        // Форма не изменена — «Сохранить» неактивна, как в вебе.
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanSave);
        Assert.False(harness.ViewModel.CanCancel);
    }

    [Fact]
    public async Task InitializeAsync_ProfileFromOtherYear_ShowsYearInCreatedAt()
    {
        var harness = new Harness();
        harness.AccountApi.Profile = () => Task.FromResult(
            ProfileOf(createdAt: new DateTimeOffset(2025, 3, 12, 9, 0, 0, TimeSpan.Zero)));

        await harness.ViewModel.InitializeAsync();

        // Год не текущий — без него дата создания старого аккаунта читалась бы как дата этого года.
        Assert.Contains("2025", harness.ViewModel.CreatedAtText);
    }

    [Fact]
    public async Task InitializeAsync_WithoutSession_MakesNoRequests()
    {
        var harness = new Harness(withTokens: false);

        await harness.ViewModel.InitializeAsync();

        Assert.Equal(0, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAuthenticated);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.ShowError);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSession()
    {
        var harness = new Harness();
        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.Null(harness.Store.Tokens);
        Assert.False(harness.ViewModel.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsErrorWithRetry()
    {
        var harness = new Harness();
        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.ErrorMessage);
        Assert.True(harness.Session.IsAuthenticated);
    }

    [Fact]
    public async Task RetryAsync_AfterError_LoadsProfile()
    {
        var harness = new Harness();
        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            new HttpRequestException("refused"));
        await harness.ViewModel.InitializeAsync();

        harness.AccountApi.Profile = () => Task.FromResult(ProfileOf());
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.True(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task SaveAsync_TrimsValues_SendsNullPhoneAndAvatar()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.FirstName = "  Ева  ";
        harness.ViewModel.LastName = "Клиентова ";
        harness.ViewModel.PhoneNumber = "   ";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.AccountApi.UpdateRequests);
        Assert.Equal("Ева", request.FirstName);
        Assert.Equal("Клиентова", request.LastName);
        Assert.Null(request.PhoneNumber);

        // Аватар не показывается в этом срезе, но уходит обратно: PUT заменяет поле целиком.
        Assert.Equal(AvatarUrl, request.AvatarUrl);
    }

    [Theory]
    [InlineData("9991234567", "+79991234567")]
    [InlineData("8 (999) 123-45-67", "+7 (999) 123-45-67")]
    [InlineData("+7 999 123-45-67", "+7 999 123-45-67")]
    public void PhoneNumber_Input_GetsRussianCountryCode(string input, string expected)
    {
        var harness = new Harness();

        harness.ViewModel.PhoneNumber = input;

        // То же поле, что в «Записать клиента»: набранная национальная часть получает +7,
        // а нестираемое начало +79 и курсор держит PhonePrefixBehavior.
        Assert.Equal(expected, harness.ViewModel.PhoneNumber);
    }

    [Fact]
    public async Task SaveAsync_InvalidPhone_ShowsFieldErrorWithoutRequest()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.FirstName = "Анна";
        harness.ViewModel.PhoneNumber = "999123";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        // Начатый, но неполный номер отвергается до запроса: общий доменный PhoneNumber
        // и общий текст PhoneInputText, без второй копии правила в профиле.
        Assert.Equal("+7999123", harness.ViewModel.PhoneNumber);
        Assert.True(harness.ViewModel.HasPhoneNumberError);
        Assert.Equal(PhoneInputText.Invalid, harness.ViewModel.PhoneNumberError);
        Assert.Empty(harness.AccountApi.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_PrefixOnlyPhone_SendsNullPhone()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.PhoneNumber = PhoneInput.MobilePrefix;

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        // Один +79 без цифр национальной части — это пустое необязательное поле, а не
        // неполный номер: человеку не нужно стирать начало вручную.
        var request = Assert.Single(harness.AccountApi.UpdateRequests);
        Assert.Null(request.PhoneNumber);
        Assert.Equal(string.Empty, harness.ViewModel.PhoneNumber);
        Assert.False(harness.ViewModel.HasPhoneNumberError);
    }

    [Fact]
    public async Task PhoneNumber_PrefixOnlyOnEmptyProfile_IsNotDirty()
    {
        var harness = new Harness();
        harness.AccountApi.Profile = () => Task.FromResult(ProfileOf(phoneNumber: null));
        await harness.ViewModel.InitializeAsync();

        // Фокус на пустом поле показывает +79: это состояние поля, а не правка, иначе
        // «Сохранить» просыпалась бы у нетронутой формы.
        harness.ViewModel.PhoneNumber = PhoneInput.MobilePrefix;

        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanSave);
    }

    [Fact]
    public async Task SaveAsync_NotDirty_DoesNothing()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(harness.AccountApi.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_EmptyFirstName_ShowsFieldErrorWithoutRequest()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.FirstName = "   ";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.HasFirstNameError);
        Assert.Equal("Введите имя.", harness.ViewModel.FirstNameError);
        Assert.Empty(harness.AccountApi.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_EmptyLastName_ShowsFieldErrorWithoutRequest()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.LastName = string.Empty;

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Введите фамилию.", harness.ViewModel.LastNameError);
        Assert.Empty(harness.AccountApi.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_ServerLastNameError_MapsToField()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.LastName = "Другая";
        harness.AccountApi.Update = () => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, "user.updateProfile.lastName"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Введите фамилию.", harness.ViewModel.LastNameError);
        Assert.False(harness.ViewModel.HasFormError);
    }

    [Fact]
    public async Task SaveAsync_ServerPhoneError_MapsToPhoneField()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.PhoneNumber = "+79991234567";
        harness.AccountApi.Update = () => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, "user.updateProfile.phoneNumber"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        // Локальная проверка такой номер пропускает, поэтому 400 доходит до поля: серверный
        // код остаётся страховкой на случай правила, которого нет в домене телефона.
        Assert.True(harness.ViewModel.HasPhoneNumberError);
        Assert.Equal(
            "Похоже, в номере опечатка. Нужен российский мобильный номер, например +7 900 000-00-00.",
            harness.ViewModel.PhoneNumberError);
        Assert.False(harness.ViewModel.HasFormError);
        Assert.Null(harness.ViewModel.Announcement);
    }

    [Fact]
    public async Task SaveAsync_ServerErrorsForSeveralFields_ShowsEveryFieldError()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.LastName = "Другая";
        harness.ViewModel.PhoneNumber = "+79991234567";
        harness.AccountApi.Update = () => Task.FromException(
            new BarberSearchApiException(
                HttpStatusCode.BadRequest,
                "Request failed.",
                ["user.updateProfile.lastName", "user.updateProfile.phoneNumber"],
                new Dictionary<string, string[]>()));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        // Сервер присылает все ошибки сразу — исправлять обе причины, а не по одной за нажатие.
        Assert.Equal("Введите фамилию.", harness.ViewModel.LastNameError);
        Assert.True(harness.ViewModel.HasPhoneNumberError);
        Assert.False(harness.ViewModel.HasFormError);
    }

    [Fact]
    public async Task PhoneEdit_AfterServerError_ClearsPhoneFieldError()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.PhoneNumber = "+79991234567";
        harness.AccountApi.Update = () => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, "user.updateProfile.phoneNumber"));
        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        harness.ViewModel.PhoneNumber = "+79991234568";

        Assert.False(harness.ViewModel.HasPhoneNumberError);
    }

    [Fact]
    public async Task SaveAsync_Success_UpdatesBaselineAndShowsAnnouncement()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.FirstName = " Анна ";
        harness.ViewModel.LastName = "Клиентова";
        harness.ViewModel.PhoneNumber = " +7 900 000-00-00 ";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Изменения сохранены.", harness.ViewModel.Announcement);
        Assert.Equal("Анна", harness.ViewModel.FirstName);
        Assert.Equal("+7 900 000-00-00", harness.ViewModel.PhoneNumber);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanSave);

        // Успех не перечитывает профиль: локальные значения и baseline уже обновлены.
        Assert.Equal(1, harness.AccountApi.ProfileCalls);
    }

    [Fact]
    public async Task SaveAsync_RateLimited_ShowsFormError()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.FirstName = "Анна";
        harness.AccountApi.Update = () => Task.FromException(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            "Слишком много запросов. Подождите минуту и повторите.",
            harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SaveAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.FirstName = "Анна";
        harness.AccountApi.Update = () => Task.FromException(new HttpRequestException("refused"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.FormError);
    }

    [Fact]
    public async Task CancelCommand_RestoresBaseline()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.FirstName = "Анна";
        harness.ViewModel.PhoneNumber = string.Empty;

        harness.ViewModel.CancelCommand.Execute(null);

        Assert.Equal("Ева", harness.ViewModel.FirstName);
        Assert.Equal("+79000000003", harness.ViewModel.PhoneNumber);
        Assert.False(harness.ViewModel.IsDirty);
    }

    [Fact]
    public async Task InitializeAsync_SilentRefresh_DoesNotOverwriteDirtyForm()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.FirstName = "Черновик";
        harness.AccountApi.Profile = () => Task.FromResult(ProfileOf(firstName: "Другое"));
        await harness.ViewModel.InitializeAsync();

        Assert.Equal("Черновик", harness.ViewModel.FirstName);
        Assert.True(harness.ViewModel.IsDirty);
    }

    [Fact]
    public async Task SendEmailChangeAsync_EmptyFields_ShowsLocalErrors()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.StartEmailChangeCommand.Execute(null);

        await harness.ViewModel.SendEmailChangeCommand.ExecuteAsync(null);

        Assert.Equal("Введите email.", harness.ViewModel.NewEmailError);
        Assert.Equal("Введите текущий пароль.", harness.ViewModel.EmailPasswordError);
        Assert.Empty(harness.AccountApi.EmailChangeRequests);
    }

    [Fact]
    public async Task SendEmailChangeAsync_InvalidEmail_ShowsTypoText()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.NewEmail = "not-an-email";
        harness.ViewModel.EmailCurrentPassword = "secret";

        await harness.ViewModel.SendEmailChangeCommand.ExecuteAsync(null);

        Assert.Equal("Похоже, в адресе опечатка.", harness.ViewModel.NewEmailError);
        Assert.Empty(harness.AccountApi.EmailChangeRequests);
    }

    [Fact]
    public async Task SendEmailChangeAsync_Success_ShowsAnnouncementAndCooldown()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.StartEmailChangeCommand.Execute(null);
        harness.ViewModel.NewEmail = " new@barbersearch.test ";
        harness.ViewModel.EmailCurrentPassword = "secret";

        await harness.ViewModel.SendEmailChangeCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.AccountApi.EmailChangeRequests);
        Assert.Equal("new@barbersearch.test", request.NewEmail);
        Assert.Equal("secret", request.CurrentPassword);

        // Показанный адрес не меняется: он изменится только после перехода по ссылке.
        Assert.Equal(UserEmail, harness.ViewModel.Email);
        Assert.Equal("Письмо отправлено на new@barbersearch.test", harness.ViewModel.EmailAnnouncement);
        Assert.False(harness.ViewModel.IsEmailFormOpen);
        Assert.True(harness.ViewModel.IsCooldownActive);
        Assert.Equal("Повторить через 60 с", harness.ViewModel.EmailButtonText);
    }

    [Fact]
    public async Task TickCooldown_CountsDownAndRestoresButton()
    {
        var harness = new Harness(successfulEmailChange: true);
        await harness.ViewModel.InitializeAsync();
        await SendEmailChangeAsync(harness);

        for (var second = 1; second <= 60; second++)
        {
            harness.ViewModel.TickCooldown();
        }

        Assert.False(harness.ViewModel.IsCooldownActive);
        Assert.Equal("Изменить email", harness.ViewModel.EmailButtonText);
    }

    [Fact]
    public async Task StartEmailChangeCommand_WhileCooldown_DoesNotOpenForm()
    {
        var harness = new Harness(successfulEmailChange: true);
        await harness.ViewModel.InitializeAsync();
        await SendEmailChangeAsync(harness);

        harness.ViewModel.StartEmailChangeCommand.Execute(null);

        Assert.False(harness.ViewModel.IsEmailFormOpen);
    }

    [Fact]
    public async Task SendEmailChangeAsync_CooldownCode_ShowsTextAndStartsCooldown()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.AccountApi.EmailChange = () => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "auth.emailChange.cooldown"));

        await SendEmailChangeAsync(harness);

        Assert.Equal("Письмо уже отправлено. Повторите через минуту.", harness.ViewModel.EmailFormError);
        Assert.True(harness.ViewModel.IsCooldownActive);
    }

    [Fact]
    public async Task SendEmailChangeAsync_TakenEmail_MapsToNewEmailField()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.AccountApi.EmailChange = () => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "user.changeEmail.emailTaken"));

        await SendEmailChangeAsync(harness);

        Assert.Equal("Этот адрес уже занят. Укажите другой.", harness.ViewModel.NewEmailError);
    }

    [Fact]
    public async Task SendEmailChangeAsync_InvalidPassword_MapsToPasswordField()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.AccountApi.EmailChange = () => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, "auth.emailChange.invalidCurrentPassword"));

        await SendEmailChangeAsync(harness);

        Assert.Equal("Текущий пароль неверный.", harness.ViewModel.EmailPasswordError);
    }

    [Fact]
    public async Task SendEmailChangeAsync_RateLimited_ShowsAttemptsText()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.AccountApi.EmailChange = () => Task.FromException(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await SendEmailChangeAsync(harness);

        Assert.Equal(
            "Слишком много попыток. Подождите минуту и повторите.",
            harness.ViewModel.EmailFormError);

        // Веб не запускает кулдаун по 429: запрос не принят, повторять можно сразу.
        Assert.False(harness.ViewModel.IsCooldownActive);
    }

    [Fact]
    public async Task SendEmailChangeAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.AccountApi.EmailChange = () => Task.FromException(new HttpRequestException("refused"));

        await SendEmailChangeAsync(harness);

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.EmailFormError);
    }

    [Fact]
    public async Task SendEmailChangeAsync_DoubleTap_SendsOnce()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.AccountApi.EmailChange = async () =>
        {
            await pending.Task;
        };

        harness.ViewModel.NewEmail = "new@barbersearch.test";
        harness.ViewModel.EmailCurrentPassword = "secret";

        var first = harness.ViewModel.SendEmailChangeCommand.ExecuteAsync(null);
        var second = harness.ViewModel.SendEmailChangeCommand.ExecuteAsync(null);

        pending.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(harness.AccountApi.EmailChangeRequests);
    }

    [Fact]
    public async Task SendEmailChangeAsync_Unauthorized_EndsSession()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        harness.AccountApi.EmailChange = () => Task.FromException(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await SendEmailChangeAsync(harness);

        Assert.False(harness.Session.IsAuthenticated);
        Assert.False(harness.ViewModel.IsAuthenticated);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls <= 3
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : Task.FromResult(ProfileOf());
        };

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(3, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.Equal("Ева", harness.ViewModel.FirstName);
        Assert.False(harness.ViewModel.ShowError);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        var harness = new Harness(autoRetry: FastRetry());
        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            ApiError(statusCode, "user.notFound"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(1, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_WhileWaiting_StopsFurtherAttempts()
    {
        var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(2, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task RetryCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        var harness = new Harness(autoRetry: HangingRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : Task.FromResult(ProfileOf());
        };

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);

        // «Повторить» — это «повторить сейчас»: цикл останавливается, чтение идёт без паузы.
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.AccountApi.ProfileCalls);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.ShowError);
    }

    [Fact]
    public async Task RetryCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<MyProfileResponse>();
        var harness = new Harness(autoRetry: StopThenGateRetry(restartGate.Task));
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls switch
            {
                1 or 2 => Task.FromException<MyProfileResponse>(new HttpRequestException("refused")),
                3 => silentAttempt.Task,
                _ => Task.FromResult(ProfileOf()),
            };
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);

        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, calls);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.IsAutoRetrying);

        // Повторяемый сбой кнопки возвращает цикл к жизни: тихая попытка падает снова, и
        // цикл повторяет до успеха.
        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
        Assert.Equal(4, calls);
        Assert.True(viewModel.ShowContent);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_FailedSilentAttempt_KeepsProfile()
    {
        var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<MyProfileResponse>();

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);
        Assert.NotNull(viewModel.ErrorMessage);

        viewModel.StartAutoRetry();

        // Попытка висит: причина и «Повторить» остаются на месте, пока ответа нет — иначе
        // блок с кнопкой исчезал бы на каждой паузе.
        Assert.Equal(1, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkText, viewModel.ErrorMessage);
        Assert.True(viewModel.IsAutoRetrying);
        Assert.False(viewModel.ShowLoading);

        retry.SetException(new HttpRequestException("refused"));

        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkText, viewModel.ErrorMessage);

        viewModel.StopAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_DuringSilentAttempt_KeepsProfile()
    {
        var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<MyProfileResponse>();

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        // Отменённая попытка не имеет права унести с собой причину: иначе экран остался бы
        // пустым и нажать было бы нечего.
        viewModel.StopAutoRetry();
        retry.SetCanceled();
        await viewModel.AutoRetryCompletion;

        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkText, viewModel.ErrorMessage);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.False(viewModel.ShowLoading);
    }

    [Fact]
    public async Task AutoRetry_Timeout_ShowsNetworkText()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new TaskCanceledException("timeout"))
                : Task.FromResult(ProfileOf());
        };

        await harness.ViewModel.InitializeAsync();

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи».
        Assert.Equal(NetworkText, harness.ViewModel.ErrorMessage);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_TerminalAfterRetryable_DoesNotRestart()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : Task.FromException<MyProfileResponse>(
                    ApiError(HttpStatusCode.NotFound, "user.notFound"));
        };

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal("Не удалось загрузить профиль.", harness.ViewModel.ErrorMessage);
        Assert.False(harness.ViewModel.IsAutoRetrying);

        // Вид сбоя запомнен для неуспеха, а не для «повторяемого»: иначе старый транспорт
        // пережил бы 404 и новое появление страницы снова запустило бы цикл на отказе.
        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(2, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task IsAutoRetrying_TogglesWithLoopState()
    {
        var harness = new Harness(autoRetry: HangingRetry());
        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        Assert.False(harness.ViewModel.IsAutoRetrying);

        await harness.ViewModel.AutoRetryCompletion;
    }

    private static Task SendEmailChangeAsync(Harness harness)
    {
        harness.ViewModel.NewEmail = "new@barbersearch.test";
        harness.ViewModel.EmailCurrentPassword = "secret";

        return harness.ViewModel.SendEmailChangeCommand.ExecuteAsync(null);
    }

    private static MyProfileResponse ProfileOf(
        string firstName = "Ева",
        DateTimeOffset? createdAt = null,
        string? phoneNumber = "+79000000003") =>
        new(
            Guid.CreateVersion7(),
            UserEmail,
            firstName,
            "Клиентова",
            phoneNumber,
            AvatarUrl,
            UserRole.Customer,
            null,
            true,
            false,
            createdAt ?? new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
            null,
            false,
            PhotoModerationStatus.Approved,
            null);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Тело сохранения профиля: все четыре поля уходят в <c>PUT</c>.
    /// </summary>
    private sealed record UpdateRequest(
        Guid UserId,
        string FirstName,
        string LastName,
        string? PhoneNumber,
        string? AvatarUrl);

    /// <summary>
    /// Тело заявки на смену email.
    /// </summary>
    private sealed record EmailChangeRequest(string NewEmail, string CurrentPassword);

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
    /// Цикл, у которого первая пауза висит до остановки, а следующая ждёт внешнего сигнала:
    /// так видно и работу цикла до нажатия кнопки, и его перезапуск после неё, без таймеров.
    /// </summary>
    private static AutoRetryLoop StopThenGateRetry(Task restartGate)
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) =>
            {
                var wait = Interlocked.Increment(ref waits);

                return wait == 1
                    ? WaitForCancellationAsync(cancellationToken)
                    : wait == 2
                        ? restartGate
                        : Task.CompletedTask;
            },
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Цикл, который висит на первой же паузе: проверяется остановка до всякой попытки.
    /// </summary>
    private static AutoRetryLoop HangingRetry() =>
        new(
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

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
    /// Собранный экран: сессия с парой, фальшивый API и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            bool withTokens = true,
            bool successfulEmailChange = false,
            AutoRetryLoop? autoRetry = null,
            DateTimeOffset? now = null)
        {
            Store = new FakeTokenStore
            {
                Tokens = withTokens ? new StoredTokens("access-1", "refresh-1") : null,
            };
            Session = new TokenSession(Store);

            if (successfulEmailChange)
            {
                AccountApi.EmailChange = () => Task.CompletedTask;
            }

            Time = new FixedTimeProvider(now ?? Now);
            ViewModel = new AccountProfileViewModel(AccountApi, Session, Time, autoRetry);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FixedTimeProvider Time { get; }

        public FakeAccountApi AccountApi { get; } = new();

        public AccountProfileViewModel ViewModel { get; }
    }

    /// <summary>
    /// «Сейчас» экрана с возможностью сдвинуть время: год служебных моментов проверяется
    /// без ожидания реальных часов.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Подменённый клиент аккаунта: запоминает тела запросов и позволяет задать отказ.
    /// </summary>
    private sealed class FakeAccountApi : IAccountApi
    {
        public int ProfileCalls { get; private set; }

        public Func<Task<MyProfileResponse>> Profile { get; set; } = () => Task.FromResult(ProfileOf());

        public Func<Task> Update { get; set; } = () => Task.CompletedTask;

        public Func<Task> EmailChange { get; set; } = () => Task.CompletedTask;

        public List<UpdateRequest> UpdateRequests { get; } = [];

        public List<EmailChangeRequest> EmailChangeRequests { get; } = [];

        public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран профиля кабинета не проверяет сессию через /auth/me.");

        public Task<MyProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken = default)
        {
            ProfileCalls++;

            return Profile();
        }

        public Task UpdateProfileAsync(
            Guid userId,
            string firstName,
            string lastName,
            string? phoneNumber,
            string? avatarUrl,
            CancellationToken cancellationToken = default)
        {
            UpdateRequests.Add(new UpdateRequest(userId, firstName, lastName, phoneNumber, avatarUrl));

            return Update();
        }

        public Task ChangePasswordAsync(
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран профиля кабинета не меняет пароль.");

        public Task RequestEmailChangeAsync(
            string newEmail,
            string currentPassword,
            CancellationToken cancellationToken = default)
        {
            EmailChangeRequests.Add(new EmailChangeRequest(newEmail, currentPassword));

            return EmailChange();
        }

        public Task RequestEmailVerificationAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран профиля кабинета не отправляет письмо подтверждения.");
    }

    /// <summary>
    /// Подменённое хранилище пары.
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
