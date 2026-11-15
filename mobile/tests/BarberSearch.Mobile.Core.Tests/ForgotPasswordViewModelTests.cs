using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Забыли пароль?»: клиентская проверка адреса, отправка ссылки,
/// карточка «Проверьте почту» и локальный кулдаун повторной отправки.
/// </summary>
/// <remarks>
/// <para>
/// Тексты сверяются дословно с вебом (<c>frontend/src/pages/forgot-password-page.tsx</c>):
/// расхождение здесь — разная копия одного продукта в двух клиентах.
/// </para>
/// <para>
/// Сервер отвечает <c>204</c> и для существующего, и для неизвестного адреса, поэтому
/// карточка успеха намеренно не различает эти случаи — тесты фиксируют это как решение,
/// а не как недоделку.
/// </para>
/// </remarks>
public sealed class ForgotPasswordViewModelTests
{
    private const string UserEmail = "mobile-test@barbersearch.test";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string FallbackText = "Не удалось отправить ссылку. Попробуйте позже.";

    [Fact]
    public async Task SendAsync_EmptyEmail_ShowsRequiredTextAndSendsNothing()
    {
        var harness = new Harness();

        await harness.SendAsync();

        Assert.Equal("Введите email.", harness.ViewModel.EmailError);
        Assert.True(harness.ViewModel.HasEmailError);
        Assert.Equal(0, harness.AuthApi.RequestCalls);
        Assert.False(harness.ViewModel.IsSent);
    }

    [Fact]
    public async Task SendAsync_MalformedEmail_ShowsInvalidTextAndSendsNothing()
    {
        var harness = new Harness();

        harness.ViewModel.Email = "not-an-email";

        await harness.SendAsync();

        Assert.Equal(
            "Проверьте email: нужен адрес вида name@example.com.",
            harness.ViewModel.EmailError);
        Assert.Equal(0, harness.AuthApi.RequestCalls);
    }

    [Fact]
    public async Task SendAsync_TooLongEmail_ShowsInvalidTextAndSendsNothing()
    {
        var harness = new Harness();

        harness.ViewModel.Email = string.Concat(new string('a', 250), "@example.com");

        await harness.SendAsync();

        Assert.True(harness.ViewModel.HasEmailError);
        Assert.Equal(0, harness.AuthApi.RequestCalls);
    }

    [Fact]
    public async Task SendAsync_ValidEmail_TrimsSendsAndShowsSentCard()
    {
        var harness = new Harness();

        harness.ViewModel.Email = string.Concat("  ", UserEmail, "  ");

        await harness.SendAsync();

        Assert.Equal(UserEmail, harness.AuthApi.LastEmail);
        Assert.Equal(1, harness.AuthApi.RequestCalls);
        Assert.True(harness.ViewModel.IsSent);
        Assert.False(harness.ViewModel.ShowForm);
        Assert.True(harness.ViewModel.ShowSentCard);
        Assert.Equal(UserEmail, harness.ViewModel.SentEmail);
        Assert.Equal(
            string.Concat(
                "Если аккаунт с адресом ",
                UserEmail,
                " существует, мы отправили на него ссылку для сброса пароля. Ссылка действует 30 минут."),
            harness.ViewModel.SentDescription);
        Assert.Equal(60, harness.ViewModel.CooldownRemaining);
        Assert.True(harness.ViewModel.IsCooldownActive);
        Assert.False(harness.ViewModel.CanResend);
        Assert.Equal("Повторить через 60 с", harness.ViewModel.ResendButtonText);
        Assert.Null(harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSubmitting);
    }

    [Fact]
    public async Task SendAsync_TooManyRequests_ShowsRateLimitTextAndStaysOnForm()
    {
        var harness = new Harness();
        harness.AuthApi.Request = _ => Task.FromException(ApiError(HttpStatusCode.TooManyRequests));
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();

        Assert.Equal(
            "Слишком много попыток. Подождите минуту и повторите.",
            harness.ViewModel.FormError);
        Assert.True(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.IsSent);
        Assert.Equal(0, harness.ViewModel.CooldownRemaining);
    }

    [Fact]
    public async Task SendAsync_Unavailable_ShowsUnavailableText()
    {
        var harness = new Harness();
        harness.AuthApi.Request = _ => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "auth.passwordReset.unavailable"));
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();

        Assert.Equal("Сброс пароля по почте пока не настроен.", harness.ViewModel.FormError);
        Assert.True(harness.ViewModel.ShowForm);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = new Harness();
        harness.AuthApi.Request = _ => Task.FromException(new HttpRequestException("boom"));
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();

        Assert.Equal(NetworkText, harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSent);
    }

    [Fact]
    public async Task SendAsync_UnexpectedFailure_ShowsFallbackText()
    {
        var harness = new Harness();
        harness.AuthApi.Request = _ => Task.FromException(ApiError(HttpStatusCode.InternalServerError));
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();

        Assert.Equal(FallbackText, harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSent);
    }

    [Fact]
    public async Task SendAsync_TransportTimeout_ShowsNetworkText()
    {
        var harness = new Harness();
        harness.AuthApi.Request = _ => Task.FromException(new OperationCanceledException());
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();

        Assert.Equal(NetworkText, harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SendAsync_DoubleTapWhilePending_SendsOnce()
    {
        var pending = new TaskCompletionSource();
        var harness = new Harness();
        harness.AuthApi.Request = _ => pending.Task;
        harness.ViewModel.Email = UserEmail;

        var first = harness.SendAsync();
        var second = harness.SendAsync();

        pending.SetResult();

        await Task.WhenAll(first, second);

        Assert.Equal(1, harness.AuthApi.RequestCalls);
        Assert.True(harness.ViewModel.IsSent);
    }

    [Fact]
    public async Task ResendAsync_NotSentYet_SendsNothing()
    {
        var harness = new Harness();
        harness.ViewModel.Email = UserEmail;

        await harness.ResendAsync();

        Assert.Equal(0, harness.AuthApi.RequestCalls);
    }

    [Fact]
    public async Task ResendAsync_DuringCooldown_SendsNothing()
    {
        var harness = new Harness();
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();
        await harness.ResendAsync();

        Assert.Equal(1, harness.AuthApi.RequestCalls);
        Assert.Equal(60, harness.ViewModel.CooldownRemaining);
    }

    [Fact]
    public async Task ResendAsync_AfterCooldown_SendsAgainAndRestartsCooldown()
    {
        var harness = new Harness();
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();
        Tick(harness.ViewModel, 60);

        Assert.Equal(0, harness.ViewModel.CooldownRemaining);
        Assert.True(harness.ViewModel.CanResend);
        Assert.Equal("Отправить ещё раз", harness.ViewModel.ResendButtonText);

        await harness.ResendAsync();

        Assert.Equal(2, harness.AuthApi.RequestCalls);
        Assert.Equal(60, harness.ViewModel.CooldownRemaining);
        Assert.False(harness.ViewModel.CanResend);
    }

    [Fact]
    public async Task ResendAsync_Failure_ShowsErrorAndKeepsSentCard()
    {
        var harness = new Harness();
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();
        Tick(harness.ViewModel, 60);

        harness.AuthApi.Request = _ => Task.FromException(ApiError(HttpStatusCode.TooManyRequests));

        await harness.ResendAsync();

        Assert.Equal(2, harness.AuthApi.RequestCalls);
        Assert.Equal(
            "Слишком много попыток. Подождите минуту и повторите.",
            harness.ViewModel.FormError);
        Assert.True(harness.ViewModel.IsSent);
        Assert.Equal(0, harness.ViewModel.CooldownRemaining);
        Assert.True(harness.ViewModel.CanResend);
    }

    [Fact]
    public async Task TickCooldown_PastZero_StopsAtZero()
    {
        var harness = new Harness();
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();
        Tick(harness.ViewModel, 70);

        Assert.Equal(0, harness.ViewModel.CooldownRemaining);
        Assert.False(harness.ViewModel.IsCooldownActive);
    }

    [Fact]
    public async Task ChangeEmail_FromSentCard_ReturnsToFormAndKeepsEmail()
    {
        var harness = new Harness();
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();

        harness.ViewModel.ChangeEmailCommand.Execute(null);

        Assert.True(harness.ViewModel.ShowForm);
        Assert.False(harness.ViewModel.IsSent);
        Assert.Equal(UserEmail, harness.ViewModel.Email);
        Assert.Null(harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SendAsync_EditEmail_ClearsFieldAndFormErrors()
    {
        var harness = new Harness();
        harness.AuthApi.Request = _ => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "auth.passwordReset.unavailable"));
        harness.ViewModel.Email = UserEmail;

        await harness.SendAsync();

        harness.ViewModel.Email = "other-test@barbersearch.test";

        Assert.Null(harness.ViewModel.EmailError);
        Assert.Null(harness.ViewModel.FormError);
    }

    private static void Tick(ForgotPasswordViewModel viewModel, int seconds)
    {
        for (var second = 0; second < seconds; second++)
        {
            viewModel.TickCooldown();
        }
    }

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, params string[] codes) =>
        new(statusCode, "Request failed.", codes, new Dictionary<string, string[]>());

    /// <summary>
    /// Собранный экран: подменённый клиент авторизации и ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            ViewModel = new ForgotPasswordViewModel(AuthApi);
        }

        public FakeAuthApi AuthApi { get; } = new();

        public ForgotPasswordViewModel ViewModel { get; }

        public Task SendAsync() => ViewModel.SendCommand.ExecuteAsync(null);

        public Task ResendAsync() => ViewModel.ResendCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Подменённый клиент авторизации: запоминает адрес, на который просили ссылку.
    /// </summary>
    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<string, Task> Request { get; set; } = _ => Task.CompletedTask;

        public int RequestCalls { get; private set; }

        public string? LastEmail { get; private set; }

        public Task<Guid> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран восстановления пароля не регистрирует.");

        public Task<TokenResponse> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран восстановления пароля не входит.");

        public Task<TokenResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран восстановления пароля не продлевает пару.");

        public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран восстановления пароля не выходит.");

        public Task RequestPasswordResetAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            RequestCalls++;
            LastEmail = email;

            return Request(email);
        }
    }
}
