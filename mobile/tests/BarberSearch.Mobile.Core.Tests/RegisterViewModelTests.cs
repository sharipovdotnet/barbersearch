using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Legal;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Регистрация»: клиентские проверки, отправка, автовход и отказы сервера.
/// </summary>
/// <remarks>
/// <para>
/// Тексты сверяются дословно с вебом (<c>frontend/src/components/register-form.tsx</c>):
/// расхождение здесь — не опечатка, а разная копия одного продукта в двух клиентах.
/// </para>
/// <para>
/// Регистрация не выдаёт пару, поэтому успех доказывается двумя запросами: <c>register</c>
/// и следующий за ним <c>login</c> тем же паролем. Тесты, где сервер отказал, проверяют
/// и отсутствие второго запроса — иначе форма входа осталась бы без причины.
/// </para>
/// </remarks>
public sealed class RegisterViewModelTests
{
    private const string UserEmail = "mobile-test@barbersearch.test";
    private const string UserFirstName = "Ева";
    private const string UserLastName = "Клиентова";
    private const string UserPassword = "Mobile-test-12!";

    [Fact]
    public async Task RegisterAsync_ValidData_RegistersAndSignsIn()
    {
        var harness = new Harness();
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.AuthApi.RegisterCalls);
        Assert.True(harness.Session.IsAuthenticated);
        Assert.Equal("access-1", harness.Session.AccessToken);
        Assert.Equal("refresh-1", harness.Store.Tokens!.RefreshToken);
        Assert.Equal(1, harness.AuthApi.LoginCalls);
        Assert.True(harness.IsRegisteredRaised);
        Assert.False(harness.ViewModel.IsSubmitting);
        Assert.False(harness.ViewModel.HasFormError);
    }

    [Fact]
    public async Task RegisterAsync_ValidData_DoesNotKeepPasswords()
    {
        var harness = new Harness();
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.Password);
        Assert.Empty(harness.ViewModel.PasswordRepeat);
        Assert.Equal(UserPassword, harness.AuthApi.LastPassword);
    }

    [Fact]
    public async Task RegisterAsync_ValidData_SendsTrimmedValuesAndPolicyVersion()
    {
        var harness = new Harness();
        harness.ViewModel.FirstName = string.Concat("  ", UserFirstName, "  ");
        harness.ViewModel.LastName = string.Concat("  ", UserLastName, "  ");
        harness.ViewModel.Email = string.Concat("  ", UserEmail, "  ");
        harness.ViewModel.Password = UserPassword;
        harness.ViewModel.PasswordRepeat = UserPassword;
        harness.ViewModel.IsConsentGiven = true;

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        var request = harness.AuthApi.LastRequest;

        Assert.NotNull(request);
        Assert.Equal(UserEmail, request.Email);
        Assert.Equal(UserFirstName, request.FirstName);
        Assert.Equal(UserLastName, request.LastName);
        Assert.Equal(UserPassword, request.Password);
        Assert.Equal(PrivacyPolicy.Version, request.ConsentVersion);
    }

    [Fact]
    public async Task RegisterAsync_EmptyForm_ShowsWordingFromWebAndKeepsNetworkIdle()
    {
        var harness = new Harness();

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Введите имя.", harness.ViewModel.FirstNameError);
        Assert.Equal("Введите фамилию.", harness.ViewModel.LastNameError);
        Assert.Equal("Введите email.", harness.ViewModel.EmailError);
        Assert.Equal("Введите пароль.", harness.ViewModel.PasswordError);
        Assert.Equal(
            "Отметьте согласие на обработку персональных данных.",
            harness.ViewModel.ConsentError);
        Assert.Equal(0, harness.AuthApi.RegisterCalls);
        Assert.False(harness.Session.IsAuthenticated);
    }

    [Fact]
    public async Task RegisterAsync_ShortPassword_ShowsWordingFromWeb()
    {
        var harness = new Harness();
        FillValid(harness.ViewModel);
        harness.ViewModel.Password = "short";
        harness.ViewModel.PasswordRepeat = "short";

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Пароль должен быть не короче 12 символов.", harness.ViewModel.PasswordError);
        Assert.Equal(0, harness.AuthApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_PasswordEqualsEmail_ShowsWordingFromWeb()
    {
        var harness = new Harness();
        FillValid(harness.ViewModel);
        harness.ViewModel.Password = UserEmail;
        harness.ViewModel.PasswordRepeat = UserEmail;

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Пароль не должен совпадать с email.", harness.ViewModel.PasswordError);
        Assert.Equal(0, harness.AuthApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_PasswordEqualsName_ShowsWordingFromWeb()
    {
        var harness = new Harness();
        FillValid(harness.ViewModel);

        // Имя длиной с пароль: короткое имя до проверки совпадения не доходит,
        // её перекрывает проверка минимальной длины — так же, как в вебе.
        harness.ViewModel.FirstName = UserPassword;
        harness.ViewModel.Password = UserPassword;
        harness.ViewModel.PasswordRepeat = UserPassword;

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(
            "Пароль не должен совпадать с именем или фамилией.",
            harness.ViewModel.PasswordError);
        Assert.Equal(0, harness.AuthApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_PasswordRepeatMismatch_ShowsWordingFromWeb()
    {
        var harness = new Harness();
        FillValid(harness.ViewModel);
        harness.ViewModel.PasswordRepeat = "Mobile-test-13!";

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Пароли не совпадают.", harness.ViewModel.PasswordRepeatError);
        Assert.Equal(0, harness.AuthApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_InvalidEmail_ShowsWordingFromWeb()
    {
        var harness = new Harness();
        FillValid(harness.ViewModel);
        harness.ViewModel.Email = "mobile-test";

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(
            "Проверьте email: нужен адрес вида name@example.com.",
            harness.ViewModel.EmailError);
        Assert.Equal(0, harness.AuthApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_EmailTaken_ShowsWordingFromWeb()
    {
        var harness = new Harness();
        harness.AuthApi.Register = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Conflict, "user.create.emailTaken"));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(
            "Аккаунт с таким email уже есть. Войдите или укажите другой адрес.",
            harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSubmitting);
        Assert.False(harness.ViewModel.IsAccountCreated);
        Assert.False(harness.Session.IsAuthenticated);
    }

    [Fact]
    public async Task RegisterAsync_TooManyAttempts_ShowsWordingFromWeb()
    {
        var harness = new Harness();
        harness.AuthApi.Register = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.TooManyRequests));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(
            "Слишком много попыток регистрации. Подождите минуту и повторите.",
            harness.ViewModel.FormError);
        Assert.Equal(0, harness.AuthApi.LoginCalls);
    }

    [Fact]
    public async Task RegisterAsync_CommonPassword_ShowsServerWordingUnderPassword()
    {
        var harness = new Harness();
        harness.AuthApi.Register = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.BadRequest, "auth.password.common"));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Этот пароль слишком распространён. Выберите другой.", harness.ViewModel.PasswordError);
        Assert.Null(harness.ViewModel.FormError);
        Assert.Equal(0, harness.AuthApi.LoginCalls);
    }

    [Fact]
    public async Task RegisterAsync_OutdatedConsentVersion_AsksToUpdateTheApp()
    {
        var harness = new Harness();
        harness.AuthApi.Register = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.BadRequest, "auth.register.consentVersion"));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(
            "Политика обработки персональных данных обновилась. Обновите приложение "
            + "и подтвердите согласие снова.",
            harness.ViewModel.FormError);
        Assert.Equal(0, harness.AuthApi.LoginCalls);
    }

    [Fact]
    public async Task RegisterAsync_NetworkFailure_ShowsNetworkWording()
    {
        var harness = new Harness();
        harness.AuthApi.Register = _ => Task.FromException<Guid>(new HttpRequestException("No network."));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSubmitting);
    }

    [Fact]
    public async Task RegisterAsync_UnknownCode_ShowsFallback()
    {
        var harness = new Harness();
        harness.AuthApi.Register = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Не удалось создать аккаунт. Попробуйте ещё раз.", harness.ViewModel.FormError);
    }

    [Fact]
    public async Task RegisterAsync_SignInFails_ShowsCreatedCardAndKeepsAccount()
    {
        var harness = new Harness();
        harness.AuthApi.Login = (_, _) => Task.FromException<TokenResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.login.invalidCredentials"));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsAccountCreated);
        Assert.True(harness.ViewModel.ShowCreatedCard);
        Assert.False(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.IsSubmitting);
        Assert.Empty(harness.ViewModel.Password);
        Assert.False(harness.Session.IsAuthenticated);
        Assert.False(harness.IsRegisteredRaised);
    }

    [Fact]
    public async Task RegisterAsync_AfterCreatedAccount_DoesNotRegisterTwice()
    {
        var harness = new Harness();
        harness.AuthApi.Login = (_, _) => Task.FromException<TokenResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.login.invalidCredentials"));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);
        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.AuthApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_SubmittingTwiceWhilePending_RegistersOnce()
    {
        var pending = new TaskCompletionSource<Guid>();
        var harness = new Harness();
        harness.AuthApi.Register = _ => pending.Task;
        FillValid(harness.ViewModel);

        var first = harness.ViewModel.RegisterCommand.ExecuteAsync(null);
        var second = harness.ViewModel.RegisterCommand.ExecuteAsync(null);

        pending.SetResult(Guid.CreateVersion7());

        await Task.WhenAll(first, second);

        Assert.Equal(1, harness.AuthApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_EditFailedField_ClearsItsError()
    {
        var harness = new Harness();

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);
        harness.ViewModel.FirstName = UserFirstName;

        Assert.Null(harness.ViewModel.FirstNameError);
        Assert.False(harness.ViewModel.HasFirstNameError);
    }

    [Fact]
    public async Task RegisterAsync_EditedEmail_ClearsFormError()
    {
        var harness = new Harness();
        harness.AuthApi.Register = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Conflict, "user.create.emailTaken"));
        FillValid(harness.ViewModel);

        await harness.ViewModel.RegisterCommand.ExecuteAsync(null);
        harness.ViewModel.Email = "other-test@barbersearch.test";

        Assert.Null(harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.HasFormError);
    }

    [Fact]
    public void PrivacyPolicyLinkText_WebEdition_ShowsTheSameDate()
    {
        var harness = new Harness();

        Assert.Equal(
            "Политика обработки персональных данных (редакция от 11 ноября 2026 г.).",
            harness.ViewModel.PrivacyPolicyLinkText);
        Assert.Equal(
            new Uri("http://localhost:5173/privacy"),
            harness.ViewModel.PrivacyPolicyUrl);
    }

    private static void FillValid(RegisterViewModel viewModel)
    {
        viewModel.FirstName = UserFirstName;
        viewModel.LastName = UserLastName;
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;
        viewModel.PasswordRepeat = UserPassword;
        viewModel.IsConsentGiven = true;
    }

    private static TokenResponse Tokens() =>
        new("access-1", "Bearer", 900, "refresh-1", 1209600);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, params string[] codes) =>
        new(statusCode, "Request failed.", codes, new Dictionary<string, string[]>());

    /// <summary>
    /// Собранный экран: фальшивые клиенты, сессия над подменённым хранилищем и ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            Session = new TokenSession(Store);

            ViewModel = new RegisterViewModel(
                AuthApi,
                Session,
                LegalOptions.FromBaseAddress("http://localhost:5173"));

            ViewModel.Registered += (_, _) => IsRegisteredRaised = true;
        }

        public FakeTokenStore Store { get; } = new();

        public TokenSession Session { get; }

        public FakeAuthApi AuthApi { get; } = new();

        public RegisterViewModel ViewModel { get; }

        public bool IsRegisteredRaised { get; private set; }
    }

    /// <summary>
    /// Подменённый клиент авторизации: считает регистрацию и автовход раздельно.
    /// </summary>
    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<RegisterRequest, Task<Guid>> Register { get; set; } =
            _ => Task.FromResult(Guid.CreateVersion7());

        public Func<string, string, Task<TokenResponse>> Login { get; set; } =
            (_, _) => Task.FromResult(Tokens());

        public int RegisterCalls { get; private set; }

        public int LoginCalls { get; private set; }

        public RegisterRequest? LastRequest { get; private set; }

        public string? LastPassword { get; private set; }

        public Task<Guid> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            LastRequest = request;

            return Register(request);
        }

        public Task<TokenResponse> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            LoginCalls++;
            LastPassword = password;

            return Login(email, password);
        }

        public Task<TokenResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран регистрации не продлевает пару.");

        public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран регистрации не выходит через /auth/logout.");
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
