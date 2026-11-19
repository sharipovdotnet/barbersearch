using System.Net;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Фотографии»: две очереди, курсор, решения и автоповтор чтений.
/// </summary>
/// <remarks>
/// <para>
/// У аватаров и портфолио разные адреса решений, но одинаковые состояния экрана, поэтому
/// здесь проверяется и то, что очереди независимы: переключение сегмента не перечитывает
/// соседнюю, а решение убирает строку только из своей.
/// </para>
/// <para>
/// Сеть подменяется <see cref="FakePhotosApi"/>: тест не поднимает бэкенд, но проходит
/// через настоящие ViewModel, курсоры и автоповтор с расписанием без ожидания. Превью
/// читает <see cref="FakeMediaApi"/> — та же очередь, но другой клиент, как в приложении.
/// </para>
/// </remarks>
public sealed class AdminPhotosViewModelTests
{
    private static readonly Guid AvatarUserId = Guid.Parse("9e100000-0000-4000-8000-0000000000a1");
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-0000000000b1");
    private static readonly Guid PhotoId = Guid.Parse("9e100000-0000-4000-8000-0000000000b2");

    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] PreviewBytes = [0xFF, 0xD8, 0xFF, 0xE0];

    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string ActionFailedText =
        "Не удалось выполнить действие с фото. Попробуйте ещё раз.";
    private const string LoadMoreErrorText =
        "Не удалось загрузить следующую страницу фотографий.";

    [Fact]
    public async Task InitializeAsync_FirstPage_LoadsAvatarsAndCursor()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage(
            [AvatarOf(), AvatarOf(lastName: "Петров", email: "petrov@example.com")],
            "cursor-1"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsAvatarsSelected);
        Assert.True(harness.ViewModel.Avatars.ShowContent);
        Assert.Equal(2, harness.ViewModel.Avatars.Items.Count);

        var first = harness.ViewModel.Avatars.Items[0];

        Assert.Equal(AvatarUserId, first.Id);
        Assert.Equal("Артём Соколов", first.OwnerName);
        Assert.Equal("artem@example.com", first.Email);
        Assert.Equal("На проверке", first.StatusText);
        Assert.StartsWith("ждёт с", first.SubmittedText);
        Assert.Equal(
            "http://localhost:5243/api/media/avatars/artem.jpg?size=md",
            first.ImageUrl);
        Assert.Contains("Одобрить: Артём Соколов", first.ApproveHint, StringComparison.Ordinal);

        Assert.True(harness.ViewModel.Avatars.CanLoadMore);
        Assert.Null(Assert.Single(harness.Api.AvatarCursors));
        Assert.Equal(AdminPhotoModerationList.PageSize, harness.Api.AvatarPageSizes[0]);
        Assert.Equal(0, harness.Api.PortfolioCalls);
    }

    [Fact]
    public async Task InitializeAsync_WithMediaApi_LoadsPreviewBytes()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.Avatars.PreviewsCompletion;

        // Превью идёт по абсолютному адресу с размером md: lg для карточки — лишние байты.
        Assert.Equal(
            ["http://localhost:5243/api/media/avatars/artem.jpg?size=md"],
            harness.Media.Requests);

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        Assert.Equal(PreviewBytes, item.ImageBytes);
        Assert.False(item.ImageFailed);
    }

    [Fact]
    public async Task InitializeAsync_PreviewFailure_MarksCardAndKeepsQueueUsable()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));
        harness.Media.GetBytes = _ => Task.FromException<byte[]>(
            ApiError(HttpStatusCode.NotFound, "media.notFound"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.Avatars.PreviewsCompletion;

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        // Превью — украшение: сбой помечает кадр, но не трогает строку и её решения.
        Assert.True(item.ImageFailed);
        Assert.Null(item.ImageBytes);
        Assert.False(item.HasError);
        Assert.True(item.ApproveCommand.CanExecute(null));
        Assert.Single(harness.ViewModel.Avatars.Items);
    }

    [Fact]
    public async Task RefreshAsync_StalePreviewAnswer_IsDropped()
    {
        var harness = new Harness();
        var pending = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));
        harness.Media.GetBytes = _ => pending.Task;

        await harness.ViewModel.InitializeAsync();

        var stale = Assert.Single(harness.ViewModel.Avatars.Items);

        harness.Api.GetAvatars = (_, _) => Task.FromResult(
            AvatarPage([AvatarOf(id: Guid.CreateVersion7(), lastName: "Петров")], null));

        await harness.ViewModel.Avatars.RefreshCommand.ExecuteAsync(null);

        var fresh = Assert.Single(harness.ViewModel.Avatars.Items);

        pending.SetResult(PreviewBytes);

        await harness.ViewModel.Avatars.PreviewsCompletion;

        // Ответ старого списка не должен попасть даже в убравшуюся строку: его отбросил
        // счётчик поколений, а не совпадение состава Items.
        Assert.Null(stale.ImageBytes);
        Assert.False(stale.ImageFailed);
        Assert.Equal(PreviewBytes, fresh.ImageBytes);
    }

    [Fact]
    public async Task InitializeAsync_WithoutMediaApi_FetchesNoPreviews()
    {
        var harness = new Harness(withMediaApi: false);

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.Avatars.PreviewsCompletion;

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        Assert.Empty(harness.Media.Requests);
        Assert.Null(item.ImageBytes);
        Assert.False(item.ImageFailed);
    }

    [Fact]
    public async Task ShowPortfolioAsync_LoadsPortfolioWithoutReloadingAvatars()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));
        harness.Api.GetPortfolio = (_, _) => Task.FromResult(PortfolioPage([PortfolioOf()], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.ShowPortfolioCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsPortfolioSelected);
        Assert.Single(harness.ViewModel.Portfolio.Items);
        Assert.Equal(1, harness.Api.AvatarCalls);
        Assert.Equal(1, harness.Api.PortfolioCalls);

        await harness.ViewModel.ShowAvatarsCommand.ExecuteAsync(null);

        // Возврат на вкладку не перечитывает ни одну очередь: обе остались загруженными.
        Assert.True(harness.ViewModel.IsAvatarsSelected);
        Assert.Equal(1, harness.Api.AvatarCalls);
        Assert.Equal(1, harness.Api.PortfolioCalls);
    }

    [Fact]
    public async Task ApproveAsync_Success_RemovesCardAfterRefreshAndAnnounces()
    {
        var harness = new Harness();
        var page = new List<AdminAvatarPhotoResponse> { AvatarOf() };

        harness.Api.GetAvatars = (_, _) => Task.FromResult(
            AvatarPage(page.ToArray(), null));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        page.Clear();

        await item.ApproveCommand.ExecuteAsync(null);

        Assert.Equal(AvatarUserId, Assert.Single(harness.Api.ApprovedAvatars));
        Assert.Empty(harness.ViewModel.Avatars.Items);
        Assert.Equal("Фото одобрено.", harness.ViewModel.Avatars.Announcement);
        Assert.False(item.IsActing);
        Assert.Equal(2, harness.Api.AvatarCalls);
    }

    [Fact]
    public async Task ApproveAsync_ServerFailure_KeepsCardAndShowsErrorNearIt()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));
        harness.Api.ApproveAvatar = _ => Task.FromException(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        await item.ApproveCommand.ExecuteAsync(null);

        // Строка остаётся: решение не прошло, и человек должен увидеть причину рядом с кадром.
        Assert.Single(harness.ViewModel.Avatars.Items);
        Assert.True(item.HasError);
        Assert.Equal(ActionFailedText, item.ErrorMessage);
        Assert.False(item.IsActing);
        Assert.Equal(1, harness.Api.AvatarCalls);
    }

    [Fact]
    public async Task ApproveAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));
        harness.Api.ApproveAvatar = _ => Task.FromException(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        await item.ApproveCommand.ExecuteAsync(null);

        Assert.Equal(NetworkErrorText, item.ErrorMessage);
        Assert.Single(harness.ViewModel.Avatars.Items);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyReviewed_LeavesQueueAndAnnounces()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));
        harness.Api.ApproveAvatar = _ => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "user.avatarModeration.invalidStatus"));

        await harness.ViewModel.InitializeAsync();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([], null));

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        await item.ApproveCommand.ExecuteAsync(null);

        // Кадр уже проверил кто-то другой: строка устарела, очередь перечитана.
        Assert.Empty(harness.ViewModel.Avatars.Items);
        Assert.Equal("Фото уже проверено. Обновите список.", harness.ViewModel.Avatars.Announcement);
        Assert.False(item.HasError);
    }

    [Fact]
    public async Task ApproveAsync_DoubleTap_DecidesOnce()
    {
        var harness = new Harness();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new List<AdminAvatarPhotoResponse> { AvatarOf() };

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage(page.ToArray(), null));
        harness.Api.ApproveAvatar = async _ => await pending.Task;

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        var first = item.ApproveCommand.ExecuteAsync(null);
        var second = item.ApproveCommand.ExecuteAsync(null);

        page.Clear();
        pending.SetResult();

        await Task.WhenAll(first, second);

        Assert.Single(harness.Api.ApprovedAvatars);
        Assert.Empty(harness.ViewModel.Avatars.Items);
    }

    [Fact]
    public async Task OpenReject_ClosesPanelOfAnotherCard()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage(
            [AvatarOf(), AvatarOf(id: Guid.CreateVersion7(), lastName: "Петров")],
            null));

        await harness.ViewModel.InitializeAsync();

        var first = harness.ViewModel.Avatars.Items[0];
        var second = harness.ViewModel.Avatars.Items[1];

        first.OpenRejectCommand.Execute(null);

        Assert.True(first.IsRejectOpen);
        Assert.False(second.IsRejectOpen);

        second.OpenRejectCommand.Execute(null);

        Assert.False(first.IsRejectOpen);
        Assert.True(second.IsRejectOpen);
    }

    [Fact]
    public async Task ConfirmReject_ReasonRules_FollowServerLimits()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        item.OpenRejectCommand.Execute(null);

        Assert.False(item.ConfirmRejectCommand.CanExecute(null));

        item.Reason = "   ";

        Assert.False(item.ConfirmRejectCommand.CanExecute(null));

        item.Reason = "Плохое качество";

        Assert.True(item.ConfirmRejectCommand.CanExecute(null));

        item.Reason = new string('а', AdminPhotoItem.RejectionReasonMaxLength + 1);

        Assert.False(item.ConfirmRejectCommand.CanExecute(null));

        item.Reason = new string('а', AdminPhotoItem.RejectionReasonMaxLength);

        Assert.True(item.ConfirmRejectCommand.CanExecute(null));
    }

    [Fact]
    public async Task RejectAsync_ValidReason_TrimsAndRemovesCard()
    {
        var harness = new Harness();
        var page = new List<AdminAvatarPhotoResponse> { AvatarOf() };

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage(page.ToArray(), null));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        item.OpenRejectCommand.Execute(null);
        item.Reason = "  Размытое фото  ";

        page.Clear();

        await item.ConfirmRejectCommand.ExecuteAsync(null);

        Assert.Equal("Размытое фото", Assert.Single(harness.Api.RejectedAvatarReasons));
        Assert.Empty(harness.ViewModel.Avatars.Items);
        Assert.False(item.IsRejectOpen);
        Assert.Equal("Фото отклонено.", harness.ViewModel.Avatars.Announcement);
    }

    [Fact]
    public async Task RejectAsync_ServerFailure_KeepsPanelAndReason()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], null));
        harness.Api.RejectAvatar = (_, _) => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, "user.avatarModeration.reasonTooLong"));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(harness.ViewModel.Avatars.Items);

        item.OpenRejectCommand.Execute(null);
        item.Reason = "Слишком длинная причина";

        await item.ConfirmRejectCommand.ExecuteAsync(null);

        // Панель остаётся открытой: причина не потеряна, решение можно повторить.
        Assert.True(item.IsRejectOpen);
        Assert.Equal("Слишком длинная причина", item.Reason);
        Assert.Equal("Слишком длинная причина — максимум 500 символов.", item.ErrorMessage);
        Assert.Single(harness.ViewModel.Avatars.Items);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsNextPageAndStopsAtEnd()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (cursor, _) => Task.FromResult(cursor is null
            ? AvatarPage([AvatarOf()], "cursor-1")
            : AvatarPage([AvatarOf(id: Guid.CreateVersion7(), lastName: "Петров")], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.Avatars.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.Avatars.Items.Count);
        Assert.False(harness.ViewModel.Avatars.CanLoadMore);
        Assert.Equal([null, "cursor-1"], harness.Api.AvatarCursors);
    }

    [Fact]
    public async Task LoadMoreAsync_Failure_KeepsRowsAndShowsFooterError()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (cursor, _) => cursor is null
            ? Task.FromResult(AvatarPage([AvatarOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminAvatarPhotoResponse>>(
                ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.Avatars.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.Avatars.Items);
        Assert.True(harness.ViewModel.Avatars.ShowLoadMoreError);
        Assert.Equal(LoadMoreErrorText, harness.ViewModel.Avatars.LoadMoreError);
    }

    [Fact]
    public async Task RetryLoadMoreAsync_AfterFailure_AppendsPage()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (cursor, _) => cursor is null
            ? Task.FromResult(AvatarPage([AvatarOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminAvatarPhotoResponse>>(
                ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.Avatars.LoadMoreCommand.ExecuteAsync(null);

        harness.Api.GetAvatars = (_, _) => Task.FromResult(
            AvatarPage([AvatarOf(id: Guid.CreateVersion7(), lastName: "Петров")], null));

        await harness.ViewModel.Avatars.RetryLoadMoreCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.Avatars.ShowLoadMoreError);
        Assert.Equal(2, harness.ViewModel.Avatars.Items.Count);
    }

    [Fact]
    public async Task RefreshAsync_ReplacesRowsAndCursor()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([AvatarOf()], "cursor-1"));

        await harness.ViewModel.InitializeAsync();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage(
            [AvatarOf(id: Guid.CreateVersion7(), lastName: "Петров")],
            null));

        await harness.ViewModel.Avatars.RefreshCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.Avatars.Items);
        Assert.Equal("Петров", harness.ViewModel.Avatars.Items[0].OwnerName.Split(' ')[1]);
        Assert.False(harness.ViewModel.Avatars.CanLoadMore);
        Assert.False(harness.ViewModel.Avatars.IsRefreshing);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndShowsSignIn()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromException<PagedResponse<AdminAvatarPhotoResponse>>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowPage);
        Assert.Empty(harness.ViewModel.Avatars.Items);
    }

    [Fact]
    public async Task InitializeAsync_Forbidden_ShowsForbiddenWithoutRetry()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromException<PagedResponse<AdminAvatarPhotoResponse>>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.Avatars.ShowForbidden);
        Assert.False(harness.ViewModel.Avatars.ShowContent);
        Assert.False(harness.ViewModel.Avatars.ShowError);
        Assert.True(harness.Session.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeAsync_EmptyQueue_ShowsEmptyState()
    {
        var harness = new Harness();

        harness.Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([], null));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.Avatars.ShowContent);
        Assert.True(harness.ViewModel.Avatars.ShowEmptyState);
    }

    [Fact]
    public async Task InitializeAsync_NoTokens_ShowsSignInWithoutRequests()
    {
        var harness = new Harness(withTokens: false);

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.Equal(0, harness.Api.AvatarCalls);
        Assert.Equal(0, harness.Api.PortfolioCalls);
    }

    [Fact]
    public async Task ApprovePortfolioAsync_Success_UsesProfileAndPhotoIds()
    {
        var harness = new Harness();
        var page = new List<AdminPortfolioPhotoResponse> { PortfolioOf() };

        harness.Api.GetPortfolio = (_, _) => Task.FromResult(
            PortfolioPage(page.ToArray(), null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.ShowPortfolioCommand.ExecuteAsync(null);

        var item = Assert.Single(harness.ViewModel.Portfolio.Items);

        page.Clear();

        await item.ApproveCommand.ExecuteAsync(null);

        Assert.Equal((BarberProfileId, PhotoId), Assert.Single(harness.Api.ApprovedPortfolioPhotos));
        Assert.Empty(harness.ViewModel.Portfolio.Items);
        Assert.Equal("Фото одобрено.", harness.ViewModel.Portfolio.Announcement);

        // Первая очередь прочитана при появлении экрана и решением не тронута.
        Assert.Equal(1, harness.Api.AvatarCalls);
        Assert.Empty(harness.Api.ApprovedAvatars);
    }

    [Fact]
    public async Task RejectPortfolioAsync_Success_SendsReasonAndProfilePath()
    {
        var harness = new Harness();
        var page = new List<AdminPortfolioPhotoResponse> { PortfolioOf() };

        harness.Api.GetPortfolio = (_, _) => Task.FromResult(
            PortfolioPage(page.ToArray(), null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.ShowPortfolioCommand.ExecuteAsync(null);

        var item = Assert.Single(harness.ViewModel.Portfolio.Items);

        item.OpenRejectCommand.Execute(null);
        item.Reason = "Не работа мастера";

        page.Clear();

        await item.ConfirmRejectCommand.ExecuteAsync(null);

        var rejected = Assert.Single(harness.Api.RejectedPortfolioPhotos);

        Assert.Equal(BarberProfileId, rejected.ProfileId);
        Assert.Equal(PhotoId, rejected.PhotoId);
        Assert.Equal("Не работа мастера", rejected.Reason);
        Assert.Empty(harness.ViewModel.Portfolio.Items);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness(avatarsAutoRetry: FastRetry());
        var calls = 0;

        harness.Api.GetAvatars = (_, _) =>
        {
            calls++;

            return calls <= 3
                ? Task.FromException<PagedResponse<AdminAvatarPhotoResponse>>(
                    new HttpRequestException("refused"))
                : Task.FromResult(AvatarPage([AvatarOf()], null));
        };

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.Avatars.ShowError);

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.Avatars.AutoRetryCompletion;

        Assert.Equal(3, harness.ViewModel.Avatars.AutoRetry.Attempts);
        Assert.Single(harness.ViewModel.Avatars.Items);
        Assert.False(harness.ViewModel.Avatars.ShowError);
        Assert.False(harness.ViewModel.Avatars.IsAutoRetrying);
    }

    [Fact]
    public async Task StartAutoRetry_Forbidden_DoesNotStart()
    {
        var harness = new Harness(avatarsAutoRetry: FastRetry());

        harness.Api.GetAvatars = (_, _) => Task.FromException<PagedResponse<AdminAvatarPhotoResponse>>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.Avatars.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.Avatars.IsAutoRetrying);
    }

    [Fact]
    public async Task StartAutoRetry_LoadMoreFailure_RetriesTheFailedCursor()
    {
        var harness = new Harness(avatarsLoadMoreAutoRetry: FastRetry());
        var calls = 0;

        harness.Api.GetAvatars = (cursor, _) =>
        {
            if (cursor is null)
            {
                return Task.FromResult(AvatarPage([AvatarOf()], "cursor-1"));
            }

            calls++;

            return calls <= 2
                ? Task.FromException<PagedResponse<AdminAvatarPhotoResponse>>(
                    new HttpRequestException("refused"))
                : Task.FromResult(
                    AvatarPage([AvatarOf(id: Guid.CreateVersion7(), lastName: "Петров")], null));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.Avatars.LoadMoreCommand.ExecuteAsync(null);

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.Avatars.LoadMoreAutoRetryCompletion;

        Assert.Equal(2, harness.ViewModel.Avatars.LoadMoreAutoRetry.Attempts);
        Assert.Equal("cursor-1", harness.Api.AvatarCursors[^1]);
        Assert.Equal(2, harness.ViewModel.Avatars.Items.Count);
        Assert.False(harness.ViewModel.Avatars.ShowLoadMoreError);
    }

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    private static PagedResponse<AdminAvatarPhotoResponse> AvatarPage(
        IReadOnlyList<AdminAvatarPhotoResponse> items,
        string? nextCursor) =>
        new(items, nextCursor);

    private static PagedResponse<AdminPortfolioPhotoResponse> PortfolioPage(
        IReadOnlyList<AdminPortfolioPhotoResponse> items,
        string? nextCursor) =>
        new(items, nextCursor);

    private static AdminAvatarPhotoResponse AvatarOf(
        Guid? id = null,
        string firstName = "Артём",
        string lastName = "Соколов",
        string email = "artem@example.com",
        string avatarUrl = "/api/media/avatars/artem.jpg",
        PhotoModerationStatus status = PhotoModerationStatus.Pending) =>
        new(
            id ?? AvatarUserId,
            firstName,
            lastName,
            email,
            avatarUrl,
            status,
            SubmittedAt);

    private static AdminPortfolioPhotoResponse PortfolioOf(
        Guid? photoId = null,
        string barberDisplayName = "Артём Соколов",
        string? description = "Стрижка машинкой") =>
        new(
            photoId ?? PhotoId,
            BarberProfileId,
            barberDisplayName,
            "owner@example.com",
            "/api/media/portfolio/work.jpg",
            description,
            true,
            PhotoModerationStatus.Pending,
            SubmittedAt);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Собранный экран: сессия с парой, фальшивый клиент фото, клиент превью и ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            bool withTokens = true,
            bool withMediaApi = true,
            AutoRetryLoop? avatarsAutoRetry = null,
            AutoRetryLoop? avatarsLoadMoreAutoRetry = null)
        {
            Store = new FakeTokenStore
            {
                Tokens = withTokens ? new StoredTokens("access-1", "refresh-1") : null,
            };
            Session = new TokenSession(Store);

            Api.GetAvatars = (_, _) => Task.FromResult(AvatarPage([], null));
            Api.GetPortfolio = (_, _) => Task.FromResult(PortfolioPage([], null));

            Media = new FakeMediaApi();

            ApiOptions = new BarberSearchApiOptions(new Uri("http://localhost:5243/"));

            ViewModel = new AdminPhotosViewModel(
                Api,
                ApiOptions,
                Session,
                avatarsAutoRetry,
                avatarsLoadMoreAutoRetry,
                mediaApi: withMediaApi ? Media : null);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public BarberSearchApiOptions ApiOptions { get; }

        public FakePhotosApi Api { get; } = new();

        public FakeMediaApi Media { get; }

        public AdminPhotosViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент фото: страницы по курсору и записи решений.
    /// </summary>
    private sealed class FakePhotosApi : IAdminPhotosApi
    {
        public int AvatarCalls { get; private set; }

        public int PortfolioCalls { get; private set; }

        public List<string?> AvatarCursors { get; } = [];

        public List<string?> PortfolioCursors { get; } = [];

        public List<int> AvatarPageSizes { get; } = [];

        public List<Guid> ApprovedAvatars { get; } = [];

        public List<string> RejectedAvatarReasons { get; } = [];

        public List<(Guid ProfileId, Guid PhotoId)> ApprovedPortfolioPhotos { get; } = [];

        public List<(Guid ProfileId, Guid PhotoId, string Reason)> RejectedPortfolioPhotos { get; } = [];

        public Func<string?, int, Task<PagedResponse<AdminAvatarPhotoResponse>>> GetAvatars { get; set; } =
            (_, _) => Task.FromResult(AvatarPage([], null));

        public Func<string?, int, Task<PagedResponse<AdminPortfolioPhotoResponse>>> GetPortfolio { get; set; } =
            (_, _) => Task.FromResult(PortfolioPage([], null));

        public Func<Guid, Task> ApproveAvatar { get; set; } = _ => Task.CompletedTask;

        public Func<Guid, string, Task> RejectAvatar { get; set; } =
            (_, _) => Task.CompletedTask;

        public Func<Guid, Guid, Task> ApprovePortfolioPhoto { get; set; } =
            (_, _) => Task.CompletedTask;

        public Func<Guid, Guid, string, Task> RejectPortfolioPhoto { get; set; } =
            (_, _, _) => Task.CompletedTask;

        public Task<PagedResponse<AdminAvatarPhotoResponse>> GetAvatarsAsync(
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            AvatarCalls++;
            AvatarCursors.Add(cursor);
            AvatarPageSizes.Add(pageSize);

            return GetAvatars(cursor, pageSize);
        }

        public Task<PagedResponse<AdminPortfolioPhotoResponse>> GetPortfolioAsync(
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            PortfolioCalls++;
            PortfolioCursors.Add(cursor);

            return GetPortfolio(cursor, pageSize);
        }

        public Task ApproveAvatarAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ApprovedAvatars.Add(userId);

            return ApproveAvatar(userId);
        }

        public Task RejectAvatarAsync(
            Guid userId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            RejectedAvatarReasons.Add(reason);

            return RejectAvatar(userId, reason);
        }

        public Task ApprovePortfolioPhotoAsync(
            Guid barberProfileId,
            Guid photoId,
            CancellationToken cancellationToken = default)
        {
            ApprovedPortfolioPhotos.Add((barberProfileId, photoId));

            return ApprovePortfolioPhoto(barberProfileId, photoId);
        }

        public Task RejectPortfolioPhotoAsync(
            Guid barberProfileId,
            Guid photoId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            RejectedPortfolioPhotos.Add((barberProfileId, photoId, reason));

            return RejectPortfolioPhoto(barberProfileId, photoId, reason);
        }
    }

    /// <summary>
    /// Подменённый клиент превью: запоминает адреса и отдаёт заданные байты или сбой.
    /// </summary>
    private sealed class FakeMediaApi : IAdminMediaApi
    {
        /// <summary>
        /// Запрошенные адреса: по ним видно и вариант размера, и отказ при отсутствии клиента.
        /// </summary>
        public List<string> Requests { get; } = [];

        /// <summary>
        /// Ответ на запрос: байты по умолчанию, исключение или незавершённая задача.
        /// </summary>
        public Func<string, Task<byte[]>> GetBytes { get; set; } =
            _ => Task.FromResult(PreviewBytes);

        public Task<byte[]> GetBytesAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(url);

            return GetBytes(url);
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
}
