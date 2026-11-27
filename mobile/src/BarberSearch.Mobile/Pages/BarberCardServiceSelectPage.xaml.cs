using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран выбора услуги в карточке мастера: «Любая услуга» и услуги, сгруппированные по
/// специализациям.
/// </summary>
/// <remarks>
/// <para>
/// Экран открыт из поля «Услуга» в секции «Свободное время», поэтому получает ViewModel
/// карточки объектом маршрута <c>owner</c>: выбор сразу применяется к карточке и
/// перезапрашивает слоты, а возврат без выбора ничего не меняет.
/// </para>
/// <para>
/// «Любая услуга» стоит вне групп — это отсутствие выбора, а не услуга справочника, как первый
/// пункт селекта в вебе. Заголовки групп — специализации, поэтому список повторяет
/// сгруппированный селект веба, которого нет у MAUI <c>Picker</c>.
/// </para>
/// </remarks>
public partial class BarberCardServiceSelectPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут экрана выбора услуги в Shell.
    /// </summary>
    public const string Route = "barber-card-service-select";

    private BarberCardViewModel? _owner;
    private bool _queryApplied;
    private bool _isNavigating;

    /// <summary>
    /// Создаёт экран выбора услуги.
    /// </summary>
    public BarberCardServiceSelectPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Принимает ViewModel карточки, к которой применяется выбор.
    /// </summary>
    /// <remarks>
    /// Без владельца выбору некуда применяться: страница сразу закрывается, вместо того
    /// чтобы показывать список, который ничего не меняет.
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
            || ownerValue is not BarberCardViewModel owner)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _owner = owner;
        _queryApplied = true;

        BindingContext = owner;
    }

    /// <summary>
    /// Применяет выбранную услугу к карточке и закрывает экран.
    /// </summary>
    /// <remarks>
    /// Выбор применяется до возврата: <see cref="BarberCardViewModel.SelectServiceAsync"/>
    /// перезапрашивает слоты тем же кодом, что и прежний <c>Picker</c>.
    /// </remarks>
    /// <param name="sender">Жест тапа по строке услуги.</param>
    /// <param name="e">Параметр жеста — выбранная услуга прайса.</param>
    private async void OnServiceTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BarberServiceOption service || _owner is null)
        {
            return;
        }

        await _owner.SelectServiceAsync(service);

        await GoBackAsync();
    }

    /// <summary>
    /// Сбрасывает выбор на «Любую услугу» и закрывает экран.
    /// </summary>
    /// <param name="sender">Жест тапа по строке «Любая услуга».</param>
    /// <param name="e">Пустые аргументы жеста.</param>
    private async void OnAnyServiceTapped(object? sender, TappedEventArgs e)
    {
        if (_owner is null)
        {
            return;
        }

        await _owner.SelectServiceAsync(_owner.AnyServiceOption);

        await GoBackAsync();
    }

    /// <summary>
    /// Возвращает на карточку, не открывая второй переход поверх первого.
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
