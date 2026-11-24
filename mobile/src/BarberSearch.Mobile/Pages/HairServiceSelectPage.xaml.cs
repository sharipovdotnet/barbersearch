using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран выбора услуги для фильтра «Поиск»: «Любая услуга» и услуги, сгруппированные
/// по специализациям.
/// </summary>
/// <remarks>
/// <para>
/// Экран открыт из фильтра «Услуга» на вкладке «Поиск», поэтому получает её ViewModel
/// объектом маршрута <c>owner</c>: выбор сразу применяется к фильтру, а возврат без выбора
/// ничего не меняет.
/// </para>
/// <para>
/// «Любая услуга» стоит вне групп — это отсутствие фильтра, а не услуга справочника,
/// как первый пункт селекта в вебе. Заголовки групп — специализации, поэтому список
/// повторяет сгруппированный селект веба, которого нет у MAUI <c>Picker</c>.
/// </para>
/// </remarks>
public partial class HairServiceSelectPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут экрана выбора услуги в Shell.
    /// </summary>
    public const string Route = "hair-service-select";

    private SearchViewModel? _owner;
    private bool _queryApplied;
    private bool _isNavigating;

    /// <summary>
    /// Создаёт экран выбора услуги.
    /// </summary>
    public HairServiceSelectPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Принимает ViewModel экрана «Поиск», к которому применяется выбор.
    /// </summary>
    /// <remarks>
    /// Без владельца выбору некуда применяться: страница сразу закрывается, вместо того
    /// чтобы показывать список, который ничего не фильтрует.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("owner", out var ownerValue)
            || ownerValue is not SearchViewModel owner)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _owner = owner;
        _queryApplied = true;

        BindingContext = owner;
    }

    /// <summary>
    /// Применяет выбранную услугу к владельцу и закрывает экран.
    /// </summary>
    /// <param name="sender">Жест тапа по строке услуги.</param>
    /// <param name="e">Параметр жеста — выбранная услуга справочника.</param>
    private async void OnServiceTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not SearchCatalogOption service)
        {
            return;
        }

        _owner?.SelectHairService(service);

        await GoBackAsync();
    }

    /// <summary>
    /// Сбрасывает фильтр услуги на «Любую» и закрывает экран.
    /// </summary>
    /// <param name="sender">Жест тапа по строке «Любая услуга».</param>
    /// <param name="e">Пустые аргументы жеста.</param>
    private async void OnAnyServiceTapped(object? sender, TappedEventArgs e)
    {
        _owner?.SelectHairService(null);

        await GoBackAsync();
    }

    /// <summary>
    /// Возвращает на вкладку «Поиск», не открывая второй переход поверх первого.
    /// </summary>
    private async Task GoBackAsync()
    {
        if (_isNavigating)
        {
            return;
        }

        _isNavigating = true;

        try
        {
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _isNavigating = false;
        }
    }
}
