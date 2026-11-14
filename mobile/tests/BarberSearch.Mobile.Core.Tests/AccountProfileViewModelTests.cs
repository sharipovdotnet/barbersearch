using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Auth;
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

    [Fact]
    public async Task InitializeAsync_WithSession_FillsFormFromProfile()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.Equal("Ева", harness.ViewModel.FirstName);
        Assert.Equal("Клиентова", harness.ViewModel.LastName);
        Assert.Equal("+70000000003", harness.ViewModel.PhoneNumber);
        Assert.Equal(UserEmail, harness.ViewModel.Email);
        Assert.Equal("Клиент", harness.ViewModel.RoleText);
        Assert.NotEmpty(harness.ViewModel.CreatedAtText);
        Assert.Equal("Ещё не было", harness.ViewModel.LastLoginText);

        // Форма не изменена — «Сохранить» неактивна, как в вебе.
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanSave);
        Assert.False(harness.ViewModel.CanCancel);
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
        Assert.Equal("+70000000003", harness.ViewModel.PhoneNumber);
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

    private static Task SendEmailChangeAsync(Harness harness)
    {
        harness.ViewModel.NewEmail = "new@barbersearch.test";
        harness.ViewModel.EmailCurrentPassword = "secret";

        return harness.ViewModel.SendEmailChangeCommand.ExecuteAsync(null);
    }

    private static MyProfileResponse ProfileOf(string firstName = "Ева") =>
        new(
            Guid.CreateVersion7(),
            UserEmail,
            firstName,
            "Клиентова",
            "+70000000003",
            AvatarUrl,
            UserRole.Customer,
            null,
            true,
            false,
            new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
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
    /// Собранный экран: сессия с парой, фальшивый API и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness(bool withTokens = true, bool successfulEmailChange = false)
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

            ViewModel = new AccountProfileViewModel(AccountApi, Session);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAccountApi AccountApi { get; } = new();

        public AccountProfileViewModel ViewModel { get; }
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
