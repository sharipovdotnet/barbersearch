using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Вкладка «Поиск»: фильтры витрины и найденные барберы.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI один раз (см. <c>AppShell</c>) и живёт столько же, сколько
/// приложение, поэтому подписка на событие поиска в конструкторе не снимается.
/// </para>
/// <para>
/// Прокрутка и навигация — платформенные операции, поэтому живут здесь, а не в Core:
/// ViewModel сообщает об окончании поиска событием, страница открывает карточку по тапу.
/// </para>
/// </remarks>
public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;

    /// <summary>
    /// Создаёт вкладку поиска.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public SearchPage(SearchViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        _viewModel.SearchCompleted += OnSearchCompleted;
    }

    /// <summary>
    /// Готовит экран к показу: даты пересобираются, справочники грузятся один раз.
    /// </summary>
    /// <remarks>
    /// <c>async void</c> здесь — единственная допустимая форма переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Открывает карточку барбера, по которой нажали.
    /// </summary>
    /// <param name="sender">Жест тапа.</param>
    /// <param name="e">Параметр жеста — строка выдачи.</param>
    private async void OnBarberTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BarberSearchItemResponse barber)
        {
            return;
        }

        await Shell.Current.GoToAsync(string.Concat(BarberPage.Route, "?id=", barber.Id.ToString("D")));
    }

    /// <summary>
    /// Прокручивает к результатам после поиска — как веб после отправки формы.
    /// </summary>
    /// <remarks>
    /// Событие приходит из ViewModel, а прокрутка — операция интерфейса, поэтому она
    /// выполняется через диспетчер страницы.
    /// </remarks>
    /// <param name="sender">ViewModel.</param>
    /// <param name="e">Пустые аргументы события.</param>
    private void OnSearchCompleted(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () =>
            await PageScroll.ScrollToAsync(ResultsSection, ScrollToPosition.Start, true));
}
