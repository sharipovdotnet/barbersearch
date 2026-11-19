using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Очередь премодерации фотографий: аватары и портфолио двумя сегментами.
/// </summary>
/// <remarks>
/// <para>
/// Страница открывается строкой «Фото» из хаба администрирования и живёт один визит:
/// возврат к хабу — это и есть обновление сводки после решения. Собственного экрана
/// карточки у раздела нет, поэтому страница ничего не открывает, кроме формы входа.
/// </para>
/// <para>
/// Видимость страницы держит пару «видна / скрыта»: циклы автоповтора чтений работают,
/// только пока экран на виду, включая возврат из фона. Решения не повторяются.
/// </para>
/// </remarks>
public partial class AdminPhotosPage : ContentPage
{
    /// <summary>
    /// Маршрут раздела в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Photos;

    private readonly AdminPhotosViewModel _viewModel;

    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт страницу очередей.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public AdminPhotosPage(AdminPhotosViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Читает выбранную очередь при появлении и включает автоповтор на время показа.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility = PageVisibility.Watch(this, _viewModel.StartAutoRetry, _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync();
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
    /// Возвращает к хабу: сессии больше нет, форма входа стоит на «Профиле».
    /// </summary>
    /// <remarks>
    /// Фон останавливается до перехода: Shell не поднимает <c>OnDisappearing</c> у страницы,
    /// поверх которой открыли следующую, и цикл повтора остался бы работать в ней.
    /// </remarks>
    /// <param name="sender">Кнопка «Войти».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync("..");
    }
}
