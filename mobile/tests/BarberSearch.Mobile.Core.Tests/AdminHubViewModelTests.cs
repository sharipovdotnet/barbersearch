using System.Net;
using BarberSearch.Contracts.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки хаба администрирования: снимок очередей, бейджи, состояния экрана
/// и автоповтор после сбоя.
/// </summary>
/// <remarks>
/// <para>
/// Снимок приходит одним ответом, а раскладывается по четырём постоянным строкам: порядок
/// и тексты — часть контракта экрана, поэтому проверяются и маршруты, и подписи, и числа.
/// </para>
/// <para>
/// Часы подменены <see cref="FixedTimeProvider"/>: «самый старый» и «обновлено в» зависят
/// от текущего момента, и настоящие часы сделали бы результат невоспроизводимым.
/// Цикл повтора собирается с мгновенной паузой — расписание в тесте не тратится.
/// </para>
/// </remarks>
public sealed class AdminHubViewModelTests
{
    /// <summary>
    /// Опорный момент снимка: 20 ноября 2026, 12:00 UTC.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    /// <summary>
    /// Маршруты разделов в том порядке, в каком их показывает хаб.
    /// </summary>
    private static readonly string[] SectionRoutes =
    [
        AdminRoutes.Reviews,
        AdminRoutes.Photos,
        AdminRoutes.Masters,
        AdminRoutes.Requests,
    ];

    /// <summary>
    /// Названия разделов в порядке хаба.
    /// </summary>
    private static readonly string[] SectionTitles = ["Отзывы", "Фото", "Мастера", "Заявки"];

    /// <summary>
    /// Счётчики первого снимка: отзывы, фото (сумма двух очередей), мастера, заявки.
    /// </summary>
    private static readonly int[] FirstSnapshotCounts = [12, 7, 1, 4];

    /// <summary>
    /// Бейджи первого снимка: те же числа цифрами.
    /// </summary>
    private static readonly string[] FirstSnapshotBadges = ["12", "7", "1", "4"];

    /// <summary>
    /// Подписи пустых очередей.
    /// </summary>
    private static readonly string[] EmptyQueueSubtitles =
    [
        "Очередь пуста — все отзывы проверены.",
        "Все фотографии проверены.",
        "Все профили подтверждены.",
        "Новых заявок нет.",
    ];

    [Fact]
    public async Task InitializeAsync_FirstLoad_FillsSectionsInFixedOrder()
    {
        var harness = new Harness();
        harness.SummaryApi.Get = _ => Task.FromResult(Summary(
            reviews: 12,
            oldest: OldestReview(TimeSpan.FromDays(3)),
            pendingAvatars: 2,
            pendingPortfolio: 5,
            unverifiedBarbers: 1,
            pendingRequests: 4));

        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsFirstLoading);
        Assert.False(viewModel.IsRefreshing);
        Assert.True(viewModel.ShowContent);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.ShowForbidden);
        Assert.False(viewModel.ShowSignInRequired);

        // Время снимка — по часам устройства: момент тот же, что отдал TimeProvider.
        Assert.True(viewModel.HasUpdatedAt);
        Assert.Equal(AdminText.UpdatedAt(Now), viewModel.UpdatedAtText);

        var sections = viewModel.Sections;

        Assert.Equal(4, sections.Count);
        Assert.Equal(SectionRoutes, sections.Select(section => section.Route));
        Assert.Equal(SectionTitles, sections.Select(section => section.Title));

        // Фотографии — две независимые очереди: бейдж и подпись несут их сумму, а подпись
        // ещё и разбивку, иначе человек не знает, куда идти.
        Assert.Equal(FirstSnapshotCounts, sections.Select(section => section.PendingCount));
        Assert.Equal(FirstSnapshotBadges, sections.Select(section => section.BadgeText));

        Assert.Equal("12 отзывов на модерации", sections[0].Subtitle);
        Assert.Equal("На модерации: аватары — 2, портфолио — 5", sections[1].Subtitle);
        Assert.Equal("1 профиль без подтверждения", sections[2].Subtitle);
        Assert.Equal("4 заявки в очереди", sections[3].Subtitle);

        Assert.Equal("Самый старый ждёт 3 дня", sections[0].Detail);
        Assert.True(sections[0].HasDetail);
        Assert.False(sections[1].HasDetail);
        Assert.False(sections[2].HasDetail);
        Assert.False(sections[3].HasDetail);
    }

    [Fact]
    public async Task InitializeAsync_EmptyQueues_ShowsCheckedWordingWithoutBadges()
    {
        var harness = new Harness();
        harness.SummaryApi.Get = _ => Task.FromResult(Summary());

        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        // Пустая очередь не прячется: в раздел заходят и тогда, когда ждущих нет.
        Assert.Equal(EmptyQueueSubtitles, viewModel.Sections.Select(section => section.Subtitle));

        Assert.All(viewModel.Sections, section =>
        {
            Assert.False(section.HasBadge);
            Assert.Equal("0", section.BadgeText);
            Assert.False(section.HasDetail);
        });
    }

    [Fact]
    public async Task InitializeAsync_CountAboveCap_Shows99PlusBadge()
    {
        var harness = new Harness();
        harness.SummaryApi.Get = _ => Task.FromResult(Summary(reviews: 100, pendingAvatars: 250));

        await harness.ViewModel.InitializeAsync();

        var sections = harness.ViewModel.Sections;

        Assert.True(sections[0].HasBadge);
        Assert.Equal(100, sections[0].PendingCount);
        Assert.Equal("99+", sections[0].BadgeText);

        Assert.True(sections[1].HasBadge);
        Assert.Equal(250, sections[1].PendingCount);
        Assert.Equal("99+", sections[1].BadgeText);
    }

    [Fact]
    public async Task InitializeAsync_PendingReviewsWithoutOldest_ShowsNoDetailLine()
    {
        var harness = new Harness();
        harness.SummaryApi.Get = _ => Task.FromResult(Summary(reviews: 5));

        await harness.ViewModel.InitializeAsync();

        var reviews = harness.ViewModel.Sections[0];

        // Сводка без oldest не выдумывает «самый старый»: строка деталей просто не выходит.
        Assert.Equal("5 отзывов на модерации", reviews.Subtitle);
        Assert.Null(reviews.Detail);
        Assert.False(reviews.HasDetail);
    }

    [Fact]
    public async Task RefreshAsync_Forbidden_ShowsForbiddenWithoutRetryAndClearsStaleError()
    {
        var harness = new Harness();
        harness.SummaryApi.Get = _ => Task.FromResult(Summary(reviews: 5));

        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        // Сначала экран видел сбой сети: его текст не должен пережить отказ в доступе.
        harness.SummaryApi.Get = _ => Task.FromException<AdminSummaryResponse>(
            new HttpRequestException("refused"));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkErrorText, viewModel.ErrorMessage);

        harness.SummaryApi.Get = _ => Task.FromException<AdminSummaryResponse>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsForbidden);
        Assert.True(viewModel.ShowForbidden);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.ShowContent);
        Assert.False(viewModel.IsRefreshing);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.UpdatedAtText);

        // Роль не появится от повтора: отказ в доступе цикл не запускает.
        viewModel.StartAutoRetry();

        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndRequiresSignIn()
    {
        var harness = new Harness();

        await harness.Session.EnsureLoadedAsync();

        Assert.True(harness.Session.IsAuthenticated);

        harness.SummaryApi.Get = _ => Task.FromException<AdminSummaryResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        // Пары больше нет: экран не показывает список и просит войти заново.
        Assert.False(harness.Session.IsAuthenticated);
        Assert.Null(harness.Store.Tokens);
        Assert.True(viewModel.IsSignInRequired);
        Assert.True(viewModel.ShowSignInRequired);
        Assert.False(viewModel.ShowForbidden);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.ShowContent);
        Assert.Null(viewModel.ErrorMessage);

        viewModel.StartAutoRetry();

        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task InitializeAsync_TransportFailure_ShowsNetworkTextAndArmsAutoRetry()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.SummaryApi.Get = cancellationToken =>
        {
            calls++;

            // Первое чтение падает, повтор висит: состояние цикла видно, не дожидаясь ответа.
            return calls == 1
                ? Task.FromException<AdminSummaryResponse>(new HttpRequestException("refused"))
                : WaitForCancellationAsync(cancellationToken);
        };

        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkErrorText, viewModel.ErrorMessage);
        Assert.False(viewModel.IsAutoRetrying);

        viewModel.StartAutoRetry();

        // Страница видна и сбой повторяемый: цикл взведён и уже делает первую попытку.
        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(1, viewModel.AutoRetry.Attempts);

        viewModel.StopAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.False(viewModel.IsAutoRetrying);
        Assert.Equal(1, viewModel.AutoRetry.Attempts);
    }

    [Fact]
    public async Task RetryAsync_AfterTransportFailure_ClearsErrorAndRestoresSections()
    {
        var harness = new Harness();
        var calls = 0;

        harness.SummaryApi.Get = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<AdminSummaryResponse>(new HttpRequestException("refused"))
                : Task.FromResult(Summary(reviews: 6, pendingRequests: 2));
        };

        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        Assert.True(viewModel.ShowError);

        await viewModel.RetryCommand.ExecuteAsync(null);

        // «Повторить» — это первая загрузка заново: индикатор, успех, полный снимок.
        Assert.False(viewModel.ShowError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.ShowContent);
        Assert.False(viewModel.IsFirstLoading);
        Assert.Equal(6, viewModel.Sections[0].PendingCount);
        Assert.Equal(2, viewModel.Sections[3].PendingCount);
    }

    [Fact]
    public async Task InitializeAsync_SecondAppearance_RefreshesWithoutFirstLoadSpinner()
    {
        var harness = new Harness();
        harness.SummaryApi.Get = _ => Task.FromResult(Summary(reviews: 1));

        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        Assert.Equal(1, harness.SummaryApi.Calls);

        var refreshed = new TaskCompletionSource<AdminSummaryResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        harness.SummaryApi.Get = _ => refreshed.Task;

        var spinnerShown = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AdminHubViewModel.IsFirstLoading)
                && viewModel.IsFirstLoading)
            {
                spinnerShown = true;
            }
        };

        var refresh = viewModel.InitializeAsync();

        // Возврат на страницу — тихое чтение: список и его числа остаются на месте,
        // иначе каждый возврат мигал бы индикатором первой загрузки.
        Assert.False(viewModel.IsFirstLoading);
        Assert.False(viewModel.IsRefreshing);
        Assert.Equal(1, viewModel.Sections[0].PendingCount);

        refreshed.SetResult(Summary(reviews: 9));

        await refresh;

        Assert.False(spinnerShown);
        Assert.False(viewModel.IsFirstLoading);
        Assert.Equal(2, harness.SummaryApi.Calls);
        Assert.Equal(9, viewModel.Sections[0].PendingCount);
        Assert.True(viewModel.ShowContent);
    }

    [Fact]
    public async Task InitializeAsync_StaleAnswer_DoesNotOverwriteNewerSnapshot()
    {
        var harness = new Harness();
        var older = new TaskCompletionSource<AdminSummaryResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = new TaskCompletionSource<AdminSummaryResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var answers = new Queue<TaskCompletionSource<AdminSummaryResponse>>([older, newer]);

        harness.SummaryApi.Get = _ => answers.Dequeue().Task;

        var viewModel = harness.ViewModel;

        var olderLoad = viewModel.InitializeAsync();
        var newerLoad = viewModel.InitializeAsync();

        Assert.Equal(2, harness.SummaryApi.Calls);

        newer.SetResult(Summary(reviews: 7));
        await newerLoad;

        // Поздний ответ первого чтения приходит уже после нового снимка и не применяется.
        older.SetResult(Summary(reviews: 99));
        await olderLoad;

        Assert.Equal(7, viewModel.Sections[0].PendingCount);
        Assert.Equal("7 отзывов на модерации", viewModel.Sections[0].Subtitle);
        Assert.True(viewModel.ShowContent);
    }

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    /// <returns>Цикл повтора без настоящих пауз.</returns>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Попытка, которая заканчивается только отменой: состояние «цикл работает» видно
    /// без таймеров, а остановка чисто снимает ожидание.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача запроса, завершаемая отменой.</returns>
    private static Task<AdminSummaryResponse> WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource<AdminSummaryResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
    }

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Снимок сводки; по умолчанию все очереди пусты.
    /// </summary>
    /// <param name="reviews">Сколько отзывов на модерации.</param>
    /// <param name="oldest">Самый старый отзыв очереди либо <c>null</c>.</param>
    /// <param name="pendingAvatars">Сколько аватаров ждёт проверки.</param>
    /// <param name="pendingPortfolio">Сколько работ портфолио ждёт проверки.</param>
    /// <param name="unverifiedBarbers">Сколько профилей без подтверждения.</param>
    /// <param name="pendingRequests">Сколько заявок в очереди.</param>
    /// <returns>Ответ сервера с заданными очередями.</returns>
    private static AdminSummaryResponse Summary(
        int reviews = 0,
        AdminSummaryOldestReviewResponse? oldest = null,
        int pendingAvatars = 0,
        int pendingPortfolio = 0,
        int unverifiedBarbers = 0,
        int pendingRequests = 0) =>
        new(
            new AdminSummaryReviewsResponse(reviews, oldest),
            new AdminSummaryBarbersResponse(unverifiedBarbers),
            new AdminSummarySalonsResponse(0, 0),
            new AdminSummaryPhotosResponse(pendingAvatars, pendingPortfolio),
            new AdminSummaryUsersResponse(0),
            new AdminSummaryBarberRequestsResponse(pendingRequests),
            new AdminSummaryBookingsResponse(0));

    /// <summary>
    /// Самый старый отзыв, ждущий заданное время к <see cref="Now"/>.
    /// </summary>
    /// <param name="waiting">Сколько отзыв ждёт очереди.</param>
    /// <returns>Строка «самый старый» сводки.</returns>
    private static AdminSummaryOldestReviewResponse OldestReview(TimeSpan waiting) =>
        new(
            Guid.CreateVersion7(),
            4,
            Now - waiting,
            "Игорь Мастер",
            "Ева Клиентова");

    /// <summary>
    /// Собранный экран: сессия с парой, фальшивая сводка и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            TimeProvider? timeProvider = null,
            AutoRetryLoop? autoRetry = null,
            bool withTokens = true)
        {
            Store = new FakeTokenStore
            {
                Tokens = withTokens ? new StoredTokens("access-1", "refresh-1") : null,
            };
            Session = new TokenSession(Store);

            SummaryApi.Get = _ => Task.FromResult(Summary());

            ViewModel = new AdminHubViewModel(
                SummaryApi,
                Session,
                timeProvider ?? new FixedTimeProvider(Now),
                autoRetry);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeSummaryApi SummaryApi { get; } = new();

        public AdminHubViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент сводки: ответ задаётся делегатом, вызовы считаются.
    /// </summary>
    private sealed class FakeSummaryApi : IAdminSummaryApi
    {
        public int Calls { get; private set; }

        public Func<CancellationToken, Task<AdminSummaryResponse>> Get { get; set; } =
            _ => Task.FromResult(Summary());

        public Task<AdminSummaryResponse> GetSummaryAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return Get(cancellationToken);
        }
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

    /// <summary>
    /// Часы, стоящие на заданном моменте.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
