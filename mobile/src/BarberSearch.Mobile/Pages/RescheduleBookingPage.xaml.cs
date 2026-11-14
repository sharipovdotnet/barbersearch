using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Перенести запись»: дата, свободные слоты брони и подтверждение.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута, поэтому ViewModel приходит конструктором.
/// Строку списка и владеющий ею <see cref="BookingsViewModel"/> передаёт вызывающая
/// вкладка объектами маршрута: эндпоинта «получить запись по id» нет, а строка уже несёт
/// и бронь, и загруженную карточку барбера.
/// </para>
/// <para>
/// Успех переноса применяется к строке владельца здесь, а не во ViewModel экрана: только
/// вкладка знает, как переставить время, баннер и бейдж «Ближайшая запись».
/// </para>
/// </remarks>
public partial class RescheduleBookingPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "reschedule";

    private readonly RescheduleBookingViewModel _viewModel;

    private BookingListItem? _item;
    private BookingsViewModel? _owner;
    private bool _queryApplied;
    private bool _isAppeared;

    /// <summary>
    /// Создаёт экран переноса.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public RescheduleBookingPage(RescheduleBookingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает строку списка и её ViewModel.
    /// </summary>
    /// <remarks>
    /// Без любого из двух параметров экран открывать нечего: Shell создаёт страницу до того,
    /// как станет известно содержимое, поэтому единственный разумный выход — сразу закрыться
    /// и не показывать пустую форму. Если параметры пришли после первого появления, сетка
    /// читается здесь же: второго <c>OnAppearing</c> не будет.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("booking", out var bookingValue)
            || bookingValue is not BookingListItem item
            || !query.TryGetValue("owner", out var ownerValue)
            || ownerValue is not BookingsViewModel owner)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _item = item;
        _owner = owner;
        _queryApplied = true;

        _viewModel.Initialize(item);
        _viewModel.Rescheduled += OnRescheduled;

        if (_isAppeared)
        {
            _ = _viewModel.LoadSlotsAsync();
        }
    }

    /// <summary>
    /// Читает сетку слотов при каждом появлении экрана.
    /// </summary>
    /// <remarks>
    /// Сеть трогается только когда параметры уже применены: иначе <c>Initialize</c> не был
    /// вызван и запрашивать нечего. <c>async void</c> — единственная допустимая форма
    /// переопределения, а все ошибки разбирает сам ViewModel.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _isAppeared = true;

        if (_queryApplied)
        {
            await _viewModel.LoadSlotsAsync();
        }
    }

    /// <summary>
    /// Освобождает отмену сетки слотов, когда экран уходит из стека.
    /// </summary>
    /// <remarks>
    /// ViewModel зарегистрирован transient и живёт ровно один визит на экран. Событие
    /// навигации, а не <c>OnDisappearing</c>, потому что исчезновение страницы бывает и без
    /// ухода из стека (например, приложение ушло в фон): там освобождать ещё нечего.
    /// Во время отправки не трогаем: <c>Dispose</c> отменил бы источник отмены, пока команда
    /// ещё выполняется; успешный путь освобождает ViewModel сам после закрытия экрана.
    /// </remarks>
    /// <param name="args">Тип навигации: закрытие экрана — это <c>Pop</c> или <c>PopToRoot</c>.</param>
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);

        if (args.NavigationType is NavigationType.Pop or NavigationType.PopToRoot or NavigationType.Remove
            && !_viewModel.IsSubmitting)
        {
            _viewModel.Dispose();
        }
    }

    /// <summary>
    /// Применяет подтверждённый сервером слот к строке владельца и закрывает экран.
    /// </summary>
    /// <param name="sender">ViewModel экрана переноса.</param>
    /// <param name="slot">Слот, который подтвердил сервер.</param>
    private async void OnRescheduled(object? sender, BarberSlotResponse slot)
    {
        var item = _item;
        var owner = _owner;

        if (item is null || owner is null)
        {
            return;
        }

        owner.ApplyRescheduled(item, slot);

        await Shell.Current.GoToAsync("..");

        // Успешный путь: запрос уже завершён, экран закрыт — освобождаем отмену сетки.
        _viewModel.Dispose();
    }
}
