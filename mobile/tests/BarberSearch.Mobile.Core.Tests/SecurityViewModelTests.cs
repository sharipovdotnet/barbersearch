using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Безопасность»: смена пароля с повторным входом и письмо подтверждения.
/// </summary>
/// <remarks>
/// <para>
/// Смена пароля отзывает refresh-токены и ротирует <c>SecurityStamp</c>, поэтому успех
/// проверяется не только баннером, но и новой парой в сессии: без неё приложение осталось бы
/// с мёртвой сессией. Ошибки сверяются с вебом дословно.
/// </para>
/// <para>
/// Отправка письма сверяется с <c>account-security-page.tsx</c>: <c>429</c> проверяется
/// раньше кодов, и он же включает кулдаун, как и <c>auth.emailVerification.cooldown</c>.
/// </para>
/// </remarks>
public sealed class SecurityViewModelTests
{
    private const string UserEmail = "mobile-test@barbersearch.test";
    private const string CurrentPassword = "Mobile-test-1!";
    private const string NewPassword = "Mobile-test-2!";

    private const string PasswordChangedText =
        "Пароль изменён. Остальные сессии входа завершены — здесь вы остались.";

    [Fact]
    public async Task InitializeAsync_VerifiedEmail_HidesResendButton()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.True(harness.ViewModel.IsEmailVerified);
        Assert.Equal("Адрес подтверждён.", harness.ViewModel.IsEmailVerifiedText);
        Assert.False(harness.ViewModel.ShowResendButton);
        Assert.False(harness.ViewModel.CanResend);
    }

    [Fact]
    public async Task InitializeAsync_UnverifiedEmail_ShowsResendButton()
    {
        var harness = new Harness(isEmailVerified: false);

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowResendButton);
        Assert.True(harness.ViewModel.CanResend);
        Assert.Equal("Отправить письмо ещё раз", harness.ViewModel.ResendButtonText);
        Assert.Contains("Подтвердите его по ссылке из письма", harness.ViewModel.VerificationDescription);
    }

    [Fact]
    public async Task InitializeAsync_WithoutSession_MakesNoRequests()
    {
        var harness = new Harness(withTokens: false);

        await harness.ViewModel.InitializeAsync();

        Assert.Equal(0, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAuthenticated);
        Assert.False(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSession()
    {
        var harness = new Harness();
        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.False(harness.ViewModel.IsAuthenticated);
    }

    [Fact]
    public async Task ChangePasswordAsync_EmptyFields_ShowsAllLocalErrors()
    {
        var harness = await CreateOpenedAsync();

        var outcome = await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal(PasswordChangeOutcome.Failed, outcome);
        Assert.Equal("Введите текущий пароль.", harness.ViewModel.CurrentPasswordError);
        Assert.Equal("Введите новый пароль.", harness.ViewModel.NewPasswordError);
        Assert.Equal(0, harness.AccountApi.ChangePasswordCalls);
    }

    [Fact]
    public async Task ChangePasswordAsync_ShortPassword_ShowsMinimum()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: "short-pass1", confirm: "short-pass1");

        await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal("Минимум 12 символов.", harness.ViewModel.NewPasswordError);
        Assert.Equal(0, harness.AccountApi.ChangePasswordCalls);
    }

    [Fact]
    public async Task ChangePasswordAsync_LongPassword_ShowsMaximum()
    {
        var tooLong = new string('a', 129);
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: tooLong, confirm: tooLong);

        await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal("Максимум 128 символов.", harness.ViewModel.NewPasswordError);
        Assert.Equal(0, harness.AccountApi.ChangePasswordCalls);
    }

    [Fact]
    public async Task ChangePasswordAsync_SameAsCurrent_ShowsDifferenceError()
    {
        var harness = await CreateOpenedAsync(
            current: CurrentPassword,
            next: CurrentPassword,
            confirm: CurrentPassword);

        await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal("Новый пароль должен отличаться от текущего.", harness.ViewModel.NewPasswordError);
    }

    [Fact]
    public async Task ChangePasswordAsync_ConfirmMismatch_ShowsMismatchError()
    {
        var harness = await CreateOpenedAsync(
            current: CurrentPassword,
            next: NewPassword,
            confirm: "Other-password-3!");

        await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal("Пароли не совпадают.", harness.ViewModel.ConfirmPasswordError);
    }

    [Fact]
    public async Task ChangePasswordAsync_Success_ReloginsAndReplacesPair()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);

        var outcome = await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal(PasswordChangeOutcome.Changed, outcome);
        Assert.Equal(PasswordChangedText, harness.ViewModel.PasswordAnnouncement);

        // Поля очищены, форма закрыта: держать пароль в памяти дольше не для чего.
        Assert.Empty(harness.ViewModel.CurrentPassword);
        Assert.Empty(harness.ViewModel.NewPassword);
        Assert.False(harness.ViewModel.IsPasswordFormOpen);

        // Старая пара мертва, поэтому вход выполнен заново и сохранил новую пару.
        Assert.Equal(NewPassword, harness.AuthApi.LastPassword);
        Assert.Equal(UserEmail, harness.AuthApi.LastEmail);
        Assert.Equal("new-access-token", harness.Session.AccessToken);
        Assert.Equal("new-refresh-token", harness.Store.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task ChangePasswordAsync_ReloginFails_AsksToSignInAgain()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);
        harness.AuthApi.Login = () => Task.FromException<TokenResponse>(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        var outcome = await harness.ViewModel.ChangePasswordAsync();

        // Пароль изменён, но пары нет: страница покажет сообщение и погасит сессию.
        Assert.Equal(PasswordChangeOutcome.NeedsSignIn, outcome);
        Assert.Null(harness.ViewModel.PasswordAnnouncement);
        Assert.True(harness.Session.IsAuthenticated);
    }

    [Fact]
    public async Task SignOutAsync_ClearsSession()
    {
        var harness = await CreateOpenedAsync();

        await harness.ViewModel.SignOutAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.Null(harness.Store.Tokens);
        Assert.False(harness.ViewModel.IsAuthenticated);
    }

    [Theory]
    [InlineData("auth.changePassword.invalidCurrentPassword", "Текущий пароль неверный.")]
    [InlineData("auth.password.required", "Введите новый пароль.")]
    [InlineData("auth.password.tooShort", "Минимум 12 символов.")]
    [InlineData("auth.password.tooLong", "Максимум 128 символов.")]
    [InlineData("auth.password.common", "Этот пароль слишком распространён.")]
    [InlineData("auth.password.equalsEmail", "Пароль не должен совпадать с email.")]
    [InlineData("auth.password.equalsName", "Пароль не должен совпадать с именем или фамилией.")]
    public async Task ChangePasswordAsync_ServerCode_ShowsWebText(string code, string expected)
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);
        harness.AccountApi.ChangePassword = () => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, code));

        var outcome = await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal(PasswordChangeOutcome.Failed, outcome);

        var shown = harness.ViewModel.CurrentPasswordError
            ?? harness.ViewModel.NewPasswordError
            ?? harness.ViewModel.PasswordFormError;

        Assert.Equal(expected, shown);
    }

    [Fact]
    public async Task ChangePasswordAsync_RateLimited_ShowsFormError()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);
        harness.AccountApi.ChangePassword = () => Task.FromException(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal(
            "Слишком много запросов. Подождите минуту и повторите.",
            harness.ViewModel.PasswordFormError);

        // Поля не очищены: человек поправит и повторит.
        Assert.Equal(NewPassword, harness.ViewModel.NewPassword);
    }

    [Fact]
    public async Task ChangePasswordAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);
        harness.AccountApi.ChangePassword = () => Task.FromException(new HttpRequestException("refused"));

        await harness.ViewModel.ChangePasswordAsync();

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.PasswordFormError);
    }

    [Fact]
    public async Task ChangePasswordAsync_Unauthorized_EndsSession()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);
        harness.AccountApi.ChangePassword = () => Task.FromException(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.ChangePasswordAsync();

        Assert.False(harness.Session.IsAuthenticated);
    }

    [Fact]
    public async Task ChangePasswordAsync_DoubleTap_SendsOnce()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.AccountApi.ChangePassword = async () => await pending.Task;

        var first = harness.ViewModel.ChangePasswordAsync();
        var second = harness.ViewModel.ChangePasswordAsync();

        pending.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Equal(1, harness.AccountApi.ChangePasswordCalls);
        Assert.Equal(PasswordChangeOutcome.Failed, outcomes[1]);
    }

    [Fact]
    public async Task ResendVerificationAsync_Success_ShowsNoticeAndCooldown()
    {
        var harness = await CreateOpenedAsync(isEmailVerified: false);

        await harness.ViewModel.ResendVerificationCommand.ExecuteAsync(null);

        Assert.Equal(
            "Письмо отправлено на mobile-test@barbersearch.test.",
            harness.ViewModel.VerificationNotice);
        Assert.True(harness.ViewModel.IsCooldownActive);
        Assert.Equal("Повторить через 60 с", harness.ViewModel.ResendButtonText);
        Assert.False(harness.ViewModel.CanResend);
    }

    [Fact]
    public async Task ResendVerificationAsync_CooldownCode_ShowsTextAndStartsCooldown()
    {
        var harness = await CreateOpenedAsync(isEmailVerified: false);
        harness.AccountApi.Verification = () => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "auth.emailVerification.cooldown"));

        await harness.ViewModel.ResendVerificationCommand.ExecuteAsync(null);

        Assert.Equal("Письмо уже отправлено. Повторите через минуту.", harness.ViewModel.VerificationError);
        Assert.True(harness.ViewModel.IsCooldownActive);
    }

    [Fact]
    public async Task ResendVerificationAsync_Unavailable_ShowsText()
    {
        var harness = await CreateOpenedAsync(isEmailVerified: false);
        harness.AccountApi.Verification = () => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "auth.emailVerification.unavailable"));

        await harness.ViewModel.ResendVerificationCommand.ExecuteAsync(null);

        Assert.Equal("Отправка писем пока не настроена.", harness.ViewModel.VerificationError);
        Assert.False(harness.ViewModel.IsCooldownActive);
    }

    [Fact]
    public async Task ResendVerificationAsync_ServerError_ShowsSendFailedText()
    {
        var harness = await CreateOpenedAsync(isEmailVerified: false);
        harness.AccountApi.Verification = () => Task.FromException(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.ResendVerificationCommand.ExecuteAsync(null);

        Assert.Equal("Не удалось отправить письмо. Попробуйте позже.", harness.ViewModel.VerificationError);
    }

    [Fact]
    public async Task ResendVerificationAsync_RateLimited_ShowsCooldownTextAndStartsCooldown()
    {
        var harness = await CreateOpenedAsync(isEmailVerified: false);
        harness.AccountApi.Verification = () => Task.FromException(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await harness.ViewModel.ResendVerificationCommand.ExecuteAsync(null);

        Assert.Equal("Письмо уже отправлено. Повторите через минуту.", harness.ViewModel.VerificationError);
        Assert.True(harness.ViewModel.IsCooldownActive);
    }

    [Fact]
    public async Task ResendVerificationAsync_VerifiedAddress_SendsNothing()
    {
        var harness = await CreateOpenedAsync();

        await harness.ViewModel.ResendVerificationCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.AccountApi.VerificationCalls);
        Assert.False(harness.ViewModel.ShowResendButton);
    }

    [Fact]
    public async Task TickCooldown_CountsDownAndRestoresButton()
    {
        var harness = await CreateOpenedAsync(isEmailVerified: false);
        await harness.ViewModel.ResendVerificationCommand.ExecuteAsync(null);

        for (var second = 1; second <= 60; second++)
        {
            harness.ViewModel.TickCooldown();
        }

        Assert.False(harness.ViewModel.IsCooldownActive);
        Assert.Equal("Отправить письмо ещё раз", harness.ViewModel.ResendButtonText);
        Assert.True(harness.ViewModel.CanResend);
    }

    [Fact]
    public async Task OpenPasswordForm_ThenCancel_ClearsFields()
    {
        var harness = await CreateOpenedAsync(current: CurrentPassword, next: NewPassword, confirm: NewPassword);

        harness.ViewModel.OpenPasswordFormCommand.Execute(null);

        Assert.True(harness.ViewModel.IsPasswordFormOpen);
        Assert.False(harness.ViewModel.ShowOpenPasswordButton);
        Assert.Empty(harness.ViewModel.CurrentPassword);

        harness.ViewModel.CancelPasswordChangeCommand.Execute(null);

        Assert.False(harness.ViewModel.IsPasswordFormOpen);
    }

    private static async Task<Harness> CreateOpenedAsync(
        bool isEmailVerified = true,
        string current = "",
        string next = "",
        string confirm = "")
    {
        var harness = new Harness(isEmailVerified: isEmailVerified);

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.OpenPasswordFormCommand.Execute(null);
        harness.ViewModel.CurrentPassword = current;
        harness.ViewModel.NewPassword = next;
        harness.ViewModel.ConfirmPassword = confirm;

        return harness;
    }

    private static MyProfileResponse ProfileOf(bool isEmailVerified) =>
        new(
            Guid.CreateVersion7(),
            UserEmail,
            "Ева",
            "Клиентова",
            "+70000000003",
            null,
            UserRole.Customer,
            null,
            isEmailVerified,
            false,
            new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
            null,
            false,
            PhotoModerationStatus.Approved,
            null);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Собранный экран: сессия с парой, фальшивые клиенты и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness(bool withTokens = true, bool isEmailVerified = true)
        {
            Store = new FakeTokenStore
            {
                Tokens = withTokens ? new StoredTokens("access-1", "refresh-1") : null,
            };
            Session = new TokenSession(Store);

            AccountApi.Profile = () => Task.FromResult(ProfileOf(isEmailVerified));

            ViewModel = new SecurityViewModel(AccountApi, AuthApi, Session);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAccountApi AccountApi { get; } = new();

        public FakeAuthApi AuthApi { get; } = new();

        public SecurityViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент аккаунта: смена пароля и письмо подтверждения.
    /// </summary>
    private sealed class FakeAccountApi : IAccountApi
    {
        public int ProfileCalls { get; private set; }

        public int ChangePasswordCalls { get; private set; }

        public int VerificationCalls { get; private set; }

        public Func<Task<MyProfileResponse>> Profile { get; set; } = () => Task.FromResult(ProfileOf(true));

        public Func<Task> ChangePassword { get; set; } = () => Task.CompletedTask;

        public Func<Task> Verification { get; set; } = () => Task.CompletedTask;

        public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран безопасности не проверяет сессию через /auth/me.");

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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран безопасности не сохраняет личные данные.");

        public Task ChangePasswordAsync(
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default)
        {
            ChangePasswordCalls++;

            return ChangePassword();
        }

        public Task RequestEmailChangeAsync(
            string newEmail,
            string currentPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран безопасности не меняет email.");

        public Task RequestEmailVerificationAsync(CancellationToken cancellationToken = default)
        {
            VerificationCalls++;

            return Verification();
        }
    }

    /// <summary>
    /// Подменённый клиент входа: запоминает email и пароль повторного входа.
    /// </summary>
    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<Task<TokenResponse>> Login { get; set; } =
            () => Task.FromResult(new TokenResponse("new-access-token", "Bearer", 900, "new-refresh-token", 1209600));

        public string? LastEmail { get; private set; }

        public string? LastPassword { get; private set; }

        public Task<TokenResponse> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            LastEmail = email;
            LastPassword = password;

            return Login();
        }

        public Task<TokenResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран безопасности не продлевает пару.");

        public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран безопасности не выходит через /auth/logout.");
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
