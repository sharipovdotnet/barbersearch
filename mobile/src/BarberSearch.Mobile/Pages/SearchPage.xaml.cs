using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Вкладка «Поиск»: фильтры витрины и найденные мастера.
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
    /// Область «страница видна»: из неё растёт автоповтор, пока экран на виду.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Идёт переход на экран выбора услуги: второй тап не должен открыть его ещё раз.
    /// </summary>
    private bool _isNavigating;

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
    /// Область видимости создаётся до первой загрузки: сбой, пришедший из неё, уже может
    /// запустить автоповтор. <c>async void</c> здесь — единственная допустимая форма
    /// переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(
            this,
            _viewModel.StartAutoRetry,
            _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Останавливает автоповтор, пока вкладка не видна.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> сам сообщает «скрыта» и снимает подписку на окно,
    /// поэтому отдельного вызова <c>StopAutoRetry</c> здесь нет.
    /// </remarks>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

        base.OnDisappearing();
    }

    /// <summary>
    /// Подставляет выбранную подсказку города и убирает клавиатуру.
    /// </summary>
    /// <remarks>
    /// Выбор — это конец ввода: клавиатура закрывается, иначе она осталась бы поверх формы
    /// с уже выбранным городом. Сама подстановка живёт в ViewModel и проверяется без страницы.
    /// </remarks>
    /// <param name="sender">Жест тапа по строке подсказки.</param>
    /// <param name="e">Параметр жеста — подсказка города.</param>
    private void OnCitySuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not CitySuggestionResponse suggestion)
        {
            return;
        }

        _viewModel.UseCitySuggestionCommand.Execute(suggestion);
        CityEntry.Unfocus();
    }

    /// <summary>
    /// Открывает экран выбора специализации.
    /// </summary>
    /// <remarks>
    /// Выбор специализации — отдельный экран: MAUI <c>Picker</c> не умеет <c>optgroup</c>,
    /// а веб группирует строки по категориям. Экран получает ту же ViewModel, чтобы выбранная
    /// строка сразу применилась к фильтру. Пока переход идёт, повторный тап игнорируется.
    /// </remarks>
    /// <param name="sender">Жест тапа по полю специализации.</param>
    /// <param name="e">Пустые аргументы жеста.</param>
    private async void OnSpecializationFieldTapped(object? sender, TappedEventArgs e)
    {
        // Пока справочник не готов, поле показывает «Загрузка…»/«Любая специализация»:
        // открывать пустой список незачем, даже если жест всё же дошёл до отключённого Border.
        if (_isNavigating || !_viewModel.IsCatalogsEnabled)
        {
            return;
        }

        _isNavigating = true;

        try
        {
            await Shell.Current.GoToAsync(
                SpecializationSelectPage.Route,
                new Dictionary<string, object> { ["owner"] = _viewModel });
        }
        finally
        {
            _isNavigating = false;
        }
    }

    /// <summary>
    /// Открывает экран выбора услуги.
    /// </summary>
    /// <remarks>
    /// Выбор услуги — отдельный экран, ему передаётся та же ViewModel, чтобы выбранная
    /// строка сразу применилась к фильтру. Пока переход идёт, повторный тап игнорируется.
    /// </remarks>
    /// <param name="sender">Жест тапа по полю услуги.</param>
    /// <param name="e">Пустые аргументы жеста.</param>
    private async void OnHairServiceFieldTapped(object? sender, TappedEventArgs e)
    {
        // Пока справочник не готов, поле показывает «Загрузка…»/«Любая услуга»: открывать
        // пустой список незачем, даже если жест всё же дошёл до отключённого Border.
        if (_isNavigating || !_viewModel.IsCatalogsEnabled)
        {
            return;
        }

        _isNavigating = true;

        try
        {
            await Shell.Current.GoToAsync(
                HairServiceSelectPage.Route,
                new Dictionary<string, object> { ["owner"] = _viewModel });
        }
        finally
        {
            _isNavigating = false;
        }
    }

    /// <summary>
    /// Открывает карточку мастера, по которой нажали.
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
