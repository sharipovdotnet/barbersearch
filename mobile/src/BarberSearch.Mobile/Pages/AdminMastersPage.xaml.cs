using System.Globalization;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Очередь мастеров: фильтры, строки профилей и переход в карточку.
/// </summary>
/// <remarks>
/// <para>
/// Страница живёт поверх хаба администрирования и перечитывает очередь при каждом появлении:
/// возврат из карточки после решения и есть обновление, отдельного события «очередь
/// изменилась» телефону не нужно.
/// </para>
/// <para>
/// Фильтры — те же три сегмента, что у веб-списка. Стиль выбранного сегмента выставляет
/// code-behind: <c>Style</c> — не привязка, а значения токенов берутся из готовых
/// <c>SegmentButton</c> / <c>SegmentButtonSelected</c>, как у переключателя языка.
/// </para>
/// <para>
/// Переход в карточку останавливает автоповтор до <c>GoToAsync</c>: Shell не поднимает
/// <c>OnDisappearing</c> у страницы, поверх которой открыли следующую.
/// </para>
/// </remarks>
public partial class AdminMastersPage : ContentPage
{
    /// <summary>
    /// Маршрут очереди в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Masters;

    private readonly AdminMastersViewModel _viewModel;

    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт очередь мастеров.
    /// </summary>
    /// <param name="viewModel">Состояние очереди.</param>
    public AdminMastersPage(AdminMastersViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Читает очередь при появлении и включает автоповтор на время показа.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(this, _viewModel.StartAutoRetry, _viewModel.StopAutoRetry);

        ApplyFilterStyles();

        await _viewModel.InitializeAsync();

        ApplyFilterStyles();
    }

    /// <summary>
    /// Останавливает автоповтор и снимает подписку на окно.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _visibility?.Dispose();
        _visibility = null;
    }

    /// <summary>
    /// Переключает фильтр на очередь проверки.
    /// </summary>
    /// <param name="sender">Кнопка сегмента.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnPendingFilterClicked(object? sender, EventArgs e)
    {
        await _viewModel.ShowPendingCommand.ExecuteAsync(null);

        ApplyFilterStyles();
    }

    /// <summary>
    /// Переключает фильтр на все профили.
    /// </summary>
    /// <param name="sender">Кнопка сегмента.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnAllFilterClicked(object? sender, EventArgs e)
    {
        await _viewModel.ShowAllCommand.ExecuteAsync(null);

        ApplyFilterStyles();
    }

    /// <summary>
    /// Переключает фильтр на скрытые профили.
    /// </summary>
    /// <param name="sender">Кнопка сегмента.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnHiddenFilterClicked(object? sender, EventArgs e)
    {
        await _viewModel.ShowHiddenCommand.ExecuteAsync(null);

        ApplyFilterStyles();
    }

    /// <summary>
    /// Открывает карточку профиля по нажатой строке.
    /// </summary>
    /// <param name="sender">Жест строки.</param>
    /// <param name="e">Параметр жеста — строка списка.</param>
    private async void OnMasterTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not AdminMasterItem item)
        {
            return;
        }

        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(
            AdminMasterPage.Route,
            new Dictionary<string, object>
            {
                ["id"] = item.Id.ToString("D", CultureInfo.InvariantCulture),
            });
    }

    /// <summary>
    /// Возвращает к форме входа на вкладке «Профиль»: сессии больше нет.
    /// </summary>
    /// <param name="sender">Кнопка «Войти».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnSignInClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//profile");

    /// <summary>
    /// Красит выбранный сегмент фильтра.
    /// </summary>
    /// <remarks>
    /// Тот же <see cref="SegmentedButtonStyle"/>, что у переключателей языка и темы:
    /// стили <c>SegmentButton</c> / <c>SegmentButtonSelected</c> красят через
    /// <c>AppThemeBinding</c>, поэтому смена темы перекрашивает сегмент на месте.
    /// </remarks>
    private void ApplyFilterStyles()
    {
        SegmentedButtonStyle.Apply(PendingFilterButton, _viewModel.IsPendingSelected);
        SegmentedButtonStyle.Apply(AllFilterButton, _viewModel.IsAllSelected);
        SegmentedButtonStyle.Apply(HiddenFilterButton, _viewModel.IsHiddenSelected);
    }
}
