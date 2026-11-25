using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран выбора специализации для фильтра «Поиск»: «Любая специализация» и специализации,
/// сгруппированные по категориям.
/// </summary>
/// <remarks>
/// <para>
/// Экран открыт из фильтра «Специализация» на вкладке «Поиск», поэтому получает её ViewModel
/// объектом маршрута <c>owner</c>: выбор сразу применяется к фильтру, а возврат без выбора
/// ничего не меняет.
/// </para>
/// <para>
/// «Любая специализация» стоит вне групп — это отсутствие фильтра, как первый пункт селекта
/// в вебе. Заголовки групп — категории услуг, поэтому список повторяет сгруппированный селект
/// веба, которого нет у MAUI <c>Picker</c>.
/// </para>
/// </remarks>
public partial class SpecializationSelectPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут экрана выбора специализации в Shell.
    /// </summary>
    public const string Route = "specialization-select";

    private SearchViewModel? _owner;
    private bool _queryApplied;
    private bool _isNavigating;

    /// <summary>
    /// Создаёт экран выбора специализации.
    /// </summary>
    public SpecializationSelectPage()
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
    /// Применяет выбранную специализацию к владельцу и закрывает экран.
    /// </summary>
    /// <param name="sender">Жест тапа по строке специализации.</param>
    /// <param name="e">Параметр жеста — выбранная специализация справочника.</param>
    private async void OnSpecializationTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not SearchCatalogOption specialization)
        {
            return;
        }

        _owner?.SelectSpecialization(specialization);

        await GoBackAsync();
    }

    /// <summary>
    /// Сбрасывает фильтр специализации на «Любую» и закрывает экран.
    /// </summary>
    /// <param name="sender">Жест тапа по строке «Любая специализация».</param>
    /// <param name="e">Пустые аргументы жеста.</param>
    private async void OnAnySpecializationTapped(object? sender, TappedEventArgs e)
    {
        _owner?.SelectSpecialization(null);

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
