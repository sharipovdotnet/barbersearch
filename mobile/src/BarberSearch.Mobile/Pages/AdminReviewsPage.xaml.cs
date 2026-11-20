using System.Globalization;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Очередь отзывов админки: сегменты, список и переход в карточку.
/// </summary>
/// <remarks>
/// <para>
/// Страница живёт поверх хаба администрирования и обновляет список при каждом появлении:
/// возврат из карточки после решения — это и есть обновление очереди.
/// </para>
/// <para>
/// Переход по строке — здесь, а не во ViewModel: маршруты Shell принадлежат приложению,
/// а Core про Shell не знает. Перед переходом автоповтор останавливается: Shell не поднимает
/// <c>OnDisappearing</c> у страницы, поверх которой открыли следующую.
/// </para>
/// <para>
/// Сегменты перекрашиваются в code-behind, как языковая пара на «Профиле»: стили
/// <c>SegmentButton</c> и <c>SegmentButtonSelected</c> объявлены в ресурсах и идут через
/// <c>AppThemeBinding</c>, поэтому смена темы перекрашивает выбранный сегмент сама.
/// </para>
/// </remarks>
public partial class AdminReviewsPage : ContentPage
{
    /// <summary>
    /// Маршрут очереди в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Reviews;

    private readonly AdminReviewsViewModel _viewModel;

    private IDisposable? _visibility;
    private bool _isNavigating;

    /// <summary>
    /// Создаёт очередь.
    /// </summary>
    /// <param name="viewModel">Состояние очереди.</param>
    public AdminReviewsPage(AdminReviewsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplySegments();
    }

    /// <summary>
    /// Читает очередь при появлении и включает автоповтор на время показа.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(this, _viewModel.StartAutoRetry, _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync();

        ApplySegments();
    }

    /// <summary>
    /// Останавливает автоповтор и снимает подписку на окно.
    /// </summary>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

        base.OnDisappearing();
    }

    /// <summary>
    /// Открывает карточку отзыва, которую принесла нажатая строка.
    /// </summary>
    /// <param name="sender">Строка списка.</param>
    /// <param name="e">Строка очереди в параметре жеста.</param>
    private async void OnReviewTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating || e.Parameter is not AdminReviewItem item)
        {
            return;
        }

        _isNavigating = true;

        try
        {
            _viewModel.StopAutoRetry();

            await Shell.Current.GoToAsync(
                string.Concat(
                    AdminReviewPage.Route,
                    "?id=",
                    item.Id.ToString("D", CultureInfo.InvariantCulture)));
        }
        finally
        {
            _isNavigating = false;
        }
    }

    /// <summary>
    /// Переключает сегмент на «На модерации».
    /// </summary>
    /// <param name="sender">Кнопка сегмента.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnPendingClicked(object? sender, EventArgs e) =>
        await _viewModel.ShowPendingCommand.ExecuteAsync(null);

    /// <summary>
    /// Переключает сегмент на «Скрытые».
    /// </summary>
    /// <param name="sender">Кнопка сегмента.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnPublishedClicked(object? sender, EventArgs e) =>
        await _viewModel.ShowPublishedCommand.ExecuteAsync(null);

    /// <summary>
    /// Возвращает на «Профиль»: сессии больше нет, форма входа стоит там.
    /// </summary>
    /// <param name="sender">Кнопка «Войти».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnSignInClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//profile");

    /// <summary>
    /// Перекрашивает сегменты при смене выбранной очереди.
    /// </summary>
    /// <param name="sender">ViewModel экрана.</param>
    /// <param name="e">Имя изменившегося свойства.</param>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AdminReviewsViewModel.IsPendingSelected)
            or nameof(AdminReviewsViewModel.IsPublishedSelected))
        {
            ApplySegments();
        }
    }

    /// <summary>
    /// Ставит выбранному сегменту стиль выбранного, второму — обычный.
    /// </summary>
    private void ApplySegments()
    {
        SegmentedButtonStyle.Apply(PendingButton, _viewModel.IsPendingSelected);
        SegmentedButtonStyle.Apply(PublishedButton, _viewModel.IsPublishedSelected);
    }
}
