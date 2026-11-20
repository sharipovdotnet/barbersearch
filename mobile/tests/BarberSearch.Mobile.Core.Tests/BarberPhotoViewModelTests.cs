using System.Net;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Media;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки страницы фотографии: загрузка, подпись, порядок, публикация и удаление.
/// </summary>
/// <remarks>
/// Успех поднимает <see cref="BarberPhotoViewModel.Completed"/> и не трогает список сам:
/// перечитывает портфолио страница. Отказ оставляет данные как были и раскладывает причину
/// по полю, а занятость запрещает вторую мутацию.
/// </remarks>
public sealed class BarberPhotoViewModelTests
{
    private static readonly Guid ProfileId =
        Guid.Parse("9e100000-0000-4000-8000-000000000001");

    private static readonly Guid FirstPhotoId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000b1");

    private static readonly Guid SecondPhotoId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000b2");

    private static readonly Guid ThirdPhotoId =
        Guid.Parse("9e100000-0000-4000-8000-0000000000b3");

    private const string LimitText =
        "В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.";

    private const string NotFoundText = "Фото не найдено. Обновите страницу.";

    private const string CaptionTooLongText = "Подпись не длиннее 2000 символов.";

    [Fact]
    public async Task UploadAsync_HappyPath_SendsPreparedPhotoAndRaisesCompleted()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        harness.Picker.Pick = _ => Task.FromResult<PortfolioImage?>(
            new PortfolioImage([0xFF, 0xD8, 0xFF], "portfolio.jpg", "image/jpeg"));

        await harness.ViewModel.PickPhotoCommand.ExecuteAsync(null);

        harness.ViewModel.Caption = "  Моя работа  ";

        await harness.ViewModel.UploadCommand.ExecuteAsync(null);

        var upload = Assert.Single(harness.Api.PhotoUploads);

        Assert.Equal(ProfileId, upload.ProfileId);
        Assert.Equal([0xFF, 0xD8, 0xFF], upload.Photo.Content);
        Assert.Equal("portfolio.jpg", upload.Photo.FileName);
        Assert.Equal("image/jpeg", upload.Photo.ContentType);
        Assert.Equal("Моя работа", upload.Photo.Description);
        Assert.Equal(1, harness.Completions);
        Assert.False(harness.ViewModel.IsBusy);
        Assert.False(harness.ViewModel.IsUploading);
    }

    [Fact]
    public async Task PickPhotoAsync_Cancelled_LeavesNoFileAndNoError()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        harness.Picker.Pick = _ => Task.FromResult<PortfolioImage?>(null);

        await harness.ViewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Picker.Calls);
        Assert.False(harness.ViewModel.HasPickedPhoto);
        Assert.Null(harness.ViewModel.PickedPhotoBytes);
        Assert.Equal(string.Empty, harness.ViewModel.PickedFileName);
        Assert.Null(harness.ViewModel.FileErrorText);
        Assert.Null(harness.ViewModel.ErrorText);
        Assert.False(harness.ViewModel.IsBusy);
    }

    [Fact]
    public async Task PickPhotoAsync_PreparationError_ShowsFileError()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        harness.Picker.Pick = _ => Task.FromException<PortfolioImage?>(
            new PortfolioImageException(PortfolioImageError.UnsupportedType));

        await harness.ViewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal(
            "Поддерживаются только JPEG, PNG и WebP.",
            harness.ViewModel.FileErrorText);
        Assert.False(harness.ViewModel.HasPickedPhoto);
        Assert.False(harness.ViewModel.IsBusy);
    }

    [Fact]
    public async Task PickPhotoAsync_UnknownError_ShowsReadImageError()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        harness.Picker.Pick = _ => Task.FromException<PortfolioImage?>(
            new InvalidOperationException("декодер упал"));

        await harness.ViewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal(
            "Не удалось прочитать изображение. Выберите другой файл.",
            harness.ViewModel.FileErrorText);
    }

    [Fact]
    public async Task UploadAsync_CaptionTooLong_ShowsCaptionErrorAndSkipsRequest()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        await PickAsync(harness);

        harness.ViewModel.Caption = new string('я', 2001);

        await harness.ViewModel.UploadCommand.ExecuteAsync(null);

        Assert.Empty(harness.Api.PhotoUploads);
        Assert.Equal(CaptionTooLongText, harness.ViewModel.CaptionErrorText);
        Assert.Null(harness.ViewModel.ErrorText);
        Assert.Equal(0, harness.Completions);
        Assert.False(harness.ViewModel.IsBusy);
    }

    [Fact]
    public async Task UploadAsync_ServerLimit_ShowsGeneralLimitText()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        await PickAsync(harness);

        harness.Api.UploadPhoto = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "barberProfile.addPhoto.limit"));

        await harness.ViewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(LimitText, harness.ViewModel.ErrorText);
        Assert.Null(harness.ViewModel.FileErrorText);
        Assert.Null(harness.ViewModel.CaptionErrorText);
        Assert.Equal(0, harness.Completions);
        Assert.True(harness.ViewModel.HasPickedPhoto);
    }

    [Fact]
    public async Task UploadAsync_FileCode_ShowsFileError()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        await PickAsync(harness);

        harness.Api.UploadPhoto = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(
                HttpStatusCode.BadRequest,
                "barberProfile.uploadPhoto.dimensions"));

        await harness.ViewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(
            "Стороны фото должны быть от 300 до 6000 px.",
            harness.ViewModel.FileErrorText);
        Assert.Null(harness.ViewModel.ErrorText);
    }

    [Fact]
    public async Task UploadAsync_DescriptionCode_ShowsCaptionError()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        await PickAsync(harness);

        harness.Api.UploadPhoto = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(
                HttpStatusCode.BadRequest,
                "barberProfile.addPhoto.description"));

        await harness.ViewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(CaptionTooLongText, harness.ViewModel.CaptionErrorText);
        Assert.Null(harness.ViewModel.ErrorText);
    }

    [Fact]
    public async Task SaveCaptionAsync_CaptionTooLong_ShowsCaptionErrorAndSkipsRequest()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(ProfileId, [Photo(FirstPhotoId)], FirstPhotoId);
        harness.ViewModel.Caption = new string('я', 2001);

        await harness.ViewModel.SaveCaptionCommand.ExecuteAsync(null);

        Assert.Empty(harness.Api.PhotoDescriptions);
        Assert.Equal(CaptionTooLongText, harness.ViewModel.CaptionErrorText);
        Assert.Equal(0, harness.Completions);
    }

    [Fact]
    public async Task SaveCaptionAsync_Whitespace_SendsNullAndCompletes()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId, description: "Старая")],
            FirstPhotoId);

        harness.ViewModel.Caption = "   ";

        await harness.ViewModel.SaveCaptionCommand.ExecuteAsync(null);

        var update = Assert.Single(harness.Api.PhotoDescriptions);

        Assert.Equal((ProfileId, FirstPhotoId, (string?)null), update);
        Assert.Equal(1, harness.Completions);
    }

    [Fact]
    public async Task DeleteAsync_Success_CallsApiAndCompletes()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(ProfileId, [Photo(FirstPhotoId)], FirstPhotoId);

        await harness.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal((ProfileId, FirstPhotoId), Assert.Single(harness.Api.PhotoRemovals));
        Assert.Equal(1, harness.Completions);
        Assert.Null(harness.ViewModel.ErrorText);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ShowsErrorAndDoesNotComplete()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(ProfileId, [Photo(FirstPhotoId)], FirstPhotoId);

        harness.Api.RemovePhoto = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.NotFound));

        await harness.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(NotFoundText, harness.ViewModel.ErrorText);
        Assert.Equal(0, harness.Completions);
        Assert.False(harness.ViewModel.IsBusy);
    }

    [Fact]
    public async Task MoveDownAsync_MiddlePhoto_ReordersFullList()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId), Photo(SecondPhotoId), Photo(ThirdPhotoId)],
            SecondPhotoId);

        await harness.ViewModel.MoveDownCommand.ExecuteAsync(null);

        var reorder = Assert.Single(harness.Api.PhotoReorders);

        Assert.Equal(ProfileId, reorder.ProfileId);
        Assert.Equal([FirstPhotoId, ThirdPhotoId, SecondPhotoId], reorder.PhotoIds);
        Assert.Equal(1, harness.Completions);
    }

    [Fact]
    public async Task MoveUpAsync_FirstPhoto_RefusesWithoutRequest()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId), Photo(SecondPhotoId)],
            FirstPhotoId);

        Assert.False(harness.ViewModel.CanMoveUp);
        Assert.False(harness.ViewModel.MoveUpCommand.CanExecute(null));

        await harness.ViewModel.MoveUpCommand.ExecuteAsync(null);

        Assert.Empty(harness.Api.PhotoReorders);
        Assert.Equal(0, harness.Completions);
    }

    [Fact]
    public async Task MoveDownAsync_LastPhoto_RefusesWithoutRequest()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId), Photo(SecondPhotoId)],
            SecondPhotoId);

        Assert.False(harness.ViewModel.CanMoveDown);
        Assert.False(harness.ViewModel.MoveDownCommand.CanExecute(null));

        await harness.ViewModel.MoveDownCommand.ExecuteAsync(null);

        Assert.Empty(harness.Api.PhotoReorders);
    }

    [Fact]
    public async Task MoveDownAsync_ServerFailure_ShowsGeneralErrorAndDoesNotComplete()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId), Photo(SecondPhotoId)],
            FirstPhotoId);

        harness.Api.ReorderPhotos = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.BadRequest,
                "barberProfile.reorderPhotos.invalid"));

        await harness.ViewModel.MoveDownCommand.ExecuteAsync(null);

        Assert.Equal(
            "Не удалось сохранить порядок. Обновите страницу.",
            harness.ViewModel.ErrorText);
        Assert.Equal(0, harness.Completions);
    }

    [Fact]
    public async Task InitializeEdit_BlockedPhoto_LeavesOnlyDelete()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId, isBlocked: true)],
            FirstPhotoId);

        Assert.False(harness.ViewModel.CanMoveUp);
        Assert.False(harness.ViewModel.CanMoveDown);
        Assert.False(harness.ViewModel.CanEditCaption);
        Assert.False(harness.ViewModel.CanTogglePublication);
        Assert.True(harness.ViewModel.CanDelete);

        await harness.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Single(harness.Api.PhotoRemovals);
        Assert.Equal(1, harness.Completions);
    }

    [Fact]
    public void InitializeEdit_UnapprovedPhoto_HidesPublication()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId, status: PhotoModerationStatus.Pending)],
            FirstPhotoId);

        Assert.False(harness.ViewModel.CanTogglePublication);
        Assert.True(harness.ViewModel.CanEditCaption);
        Assert.True(harness.ViewModel.CanDelete);
    }

    [Fact]
    public async Task TogglePublicationAsync_Published_Unpublishes()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId, isPublished: true)],
            FirstPhotoId);

        Assert.Equal("Скрыть", harness.ViewModel.PublicationActionText);

        await harness.ViewModel.TogglePublicationCommand.ExecuteAsync(null);

        Assert.Equal(
            (ProfileId, FirstPhotoId),
            Assert.Single(harness.Api.PhotoUnpublications));
        Assert.Empty(harness.Api.PhotoPublications);
        Assert.Equal(1, harness.Completions);
    }

    [Fact]
    public async Task TogglePublicationAsync_Hidden_Publishes()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId, isPublished: false)],
            FirstPhotoId);

        Assert.Equal("Вернуть", harness.ViewModel.PublicationActionText);

        await harness.ViewModel.TogglePublicationCommand.ExecuteAsync(null);

        Assert.Equal(
            (ProfileId, FirstPhotoId),
            Assert.Single(harness.Api.PhotoPublications));
        Assert.Empty(harness.Api.PhotoUnpublications);
        Assert.Equal(1, harness.Completions);
    }

    [Fact]
    public async Task InitializeNew_PositionCounterAndLimit()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 2);

        Assert.True(harness.ViewModel.IsNew);
        Assert.Equal("Новое фото", harness.ViewModel.TitleText);
        Assert.Equal("Фото 3 из 3", harness.ViewModel.PositionText);
        Assert.Equal("0/2000", harness.ViewModel.CaptionCounterText);

        harness.ViewModel.Caption = "Привет";

        Assert.Equal(6, harness.ViewModel.CaptionLength);
        Assert.Equal("6/2000", harness.ViewModel.CaptionCounterText);

        harness.ViewModel.InitializeNew(ProfileId, PortfolioPhotoLimits.MaxPhotos);
        await PickAsync(harness);

        Assert.Equal("Фото 11 из 11", harness.ViewModel.PositionText);
        Assert.False(harness.ViewModel.CanSubmitUpload);
        Assert.False(harness.ViewModel.UploadCommand.CanExecute(null));
    }

    [Fact]
    public void InitializeEdit_MissingPhoto_ShowsErrorAndDisablesActions()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeEdit(
            ProfileId,
            [Photo(FirstPhotoId)],
            SecondPhotoId);

        Assert.False(harness.ViewModel.IsNew);
        Assert.Equal("Фото", harness.ViewModel.TitleText);
        Assert.Null(harness.ViewModel.Photo);
        Assert.Equal(NotFoundText, harness.ViewModel.ErrorText);
        Assert.False(harness.ViewModel.CanMoveUp);
        Assert.False(harness.ViewModel.CanMoveDown);
        Assert.False(harness.ViewModel.CanEditCaption);
        Assert.False(harness.ViewModel.CanTogglePublication);
        Assert.False(harness.ViewModel.CanDelete);
    }

    [Fact]
    public async Task PickPhotoAsync_Busy_BlocksSecondCall()
    {
        var harness = new Harness();
        var release = new TaskCompletionSource<PortfolioImage?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        harness.ViewModel.InitializeNew(ProfileId, photoCount: 0);
        harness.Picker.Pick = _ => release.Task;

        var first = harness.ViewModel.PickPhotoCommand.ExecuteAsync(null);

        await harness.Picker.Entered.Task;

        var second = harness.ViewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Picker.Calls);
        Assert.True(harness.ViewModel.IsBusy);
        Assert.False(harness.ViewModel.PickPhotoCommand.CanExecute(null));

        release.SetResult(
            new PortfolioImage([1, 2], "portfolio.jpg", "image/jpeg"));

        await first;
        await second;

        Assert.True(harness.ViewModel.HasPickedPhoto);
        Assert.False(harness.ViewModel.IsBusy);
    }

    private static async Task PickAsync(Harness harness)
    {
        harness.Picker.Pick = _ => Task.FromResult<PortfolioImage?>(
            new PortfolioImage([1, 2, 3], "portfolio.jpg", "image/jpeg"));

        await harness.ViewModel.PickPhotoCommand.ExecuteAsync(null);
    }

    private static BarberProfilePhotoItem Photo(
        Guid id,
        string? description = null,
        bool isPublished = true,
        bool isBlocked = false,
        PhotoModerationStatus status = PhotoModerationStatus.Approved) =>
        new(BarberCabinetData.Photo(description, isPublished, isBlocked, status, id));

    private sealed class Harness
    {
        public Harness()
        {
            ViewModel = new BarberPhotoViewModel(Api, Picker);
            ViewModel.Completed += (_, _) => Completions++;
        }

        public FakeBarberProfileApi Api { get; } = new();

        public FakePortfolioPhotoPicker Picker { get; } = new();

        public BarberPhotoViewModel ViewModel { get; }

        public int Completions { get; private set; }
    }

    /// <summary>
    /// Подменённый пикер: отдаёт заданный кадр, отмену или ошибку и считает вызовы.
    /// </summary>
    private sealed class FakePortfolioPhotoPicker : IPortfolioPhotoPicker
    {
        public Func<CancellationToken, Task<PortfolioImage?>> Pick { get; set; } =
            static _ => Task.FromResult<PortfolioImage?>(
                new PortfolioImage([1, 2, 3], "portfolio.jpg", "image/jpeg"));

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public async Task<PortfolioImage?> PickAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Entered.TrySetResult();

            return await Pick(cancellationToken);
        }
    }
}
