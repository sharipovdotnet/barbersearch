using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки полноэкранного просмотра портфолио: счётчик, подпись и приведение индекса.
/// </summary>
/// <remarks>
/// Кадры собираются настоящим <see cref="BarberPhotoOption.From"/>: просмотр получает готовые
/// объекты и не знает ни про базу адресов, ни про ответ API. Индекс приходит из
/// <c>Photos.IndexOf</c> карточки и может устареть, если галерея успела перезагрузиться.
/// </remarks>
public sealed class PortfolioViewerViewModelTests
{
    private static readonly Uri BaseAddress = new("http://localhost:5243/");

    [Fact]
    public void Initialize_PhotosAndIndex_ShowsCounterAndCaption()
    {
        var viewModel = new PortfolioViewerViewModel();

        viewModel.Initialize(
            [Photo("Первая"), Photo("Вторая"), Photo("Третья")],
            index: 1);

        Assert.Equal(3, viewModel.Photos.Count);
        Assert.Equal(1, viewModel.Position);
        // Счётчик показывает номер с единицы, а Position — с нуля: так CarouselView
        // и человек видят разные, но согласованные числа.
        Assert.Equal("2 из 3", viewModel.CounterText);
        Assert.Equal("Вторая", viewModel.CaptionText);
        Assert.True(viewModel.HasCaption);
    }

    [Fact]
    public void Initialize_IndexBelowRange_ClampsToFirstPhoto()
    {
        var viewModel = new PortfolioViewerViewModel();

        viewModel.Initialize([Photo("Первая"), Photo("Вторая")], index: -5);

        Assert.Equal(0, viewModel.Position);
        Assert.Equal("1 из 2", viewModel.CounterText);
        Assert.Equal("Первая", viewModel.CaptionText);
    }

    [Fact]
    public void Initialize_IndexAboveRange_ClampsToLastPhoto()
    {
        var viewModel = new PortfolioViewerViewModel();

        viewModel.Initialize([Photo("Первая"), Photo("Вторая")], index: 99);

        // Галерея могла перезагрузиться и стать короче: падать из-за старого индекса нельзя.
        Assert.Equal(1, viewModel.Position);
        Assert.Equal("2 из 2", viewModel.CounterText);
        Assert.Equal("Вторая", viewModel.CaptionText);
    }

    [Fact]
    public void Position_Changed_UpdatesCounterCaptionAndVisibility()
    {
        var viewModel = new PortfolioViewerViewModel();

        viewModel.Initialize([Photo("Первая"), Photo(null), Photo("Третья")], index: 0);

        viewModel.Position = 2;

        Assert.Equal("3 из 3", viewModel.CounterText);
        Assert.Equal("Третья", viewModel.CaptionText);
        Assert.True(viewModel.HasCaption);

        viewModel.Position = 1;

        Assert.Equal("2 из 3", viewModel.CounterText);
        Assert.Equal(string.Empty, viewModel.CaptionText);
        Assert.False(viewModel.HasCaption);
    }

    [Fact]
    public void Initialize_EmptyDescription_HasCaptionFalse()
    {
        var viewModel = new PortfolioViewerViewModel();

        viewModel.Initialize([Photo(null), Photo("Подпись")], index: 0);

        // Кадр без подписи не рисует пустую строку под фото.
        Assert.Equal(string.Empty, viewModel.CaptionText);
        Assert.False(viewModel.HasCaption);
    }

    private static BarberPhotoOption Photo(string? description) =>
        BarberPhotoOption.From(
            new BarberProfilePhotoResponse(
                Guid.CreateVersion7(),
                $"/api/media/portfolio/{Guid.CreateVersion7():N}.webp",
                ThumbnailUrl: null,
                description,
                SortOrder: 0,
                IsPublished: true,
                IsBlocked: false,
                PhotoModerationStatus.Approved,
                RejectionReason: null),
            BaseAddress,
            "Тимур Барберов",
            number: 1);
}
