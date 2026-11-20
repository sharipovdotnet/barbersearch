using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Перенести запись» кабинета: дата, свободные слоты и подтверждение.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута, поэтому ViewModel приходит конструктором.
/// Строку списка и владеющий ею <see cref="BarberBookingsViewModel"/> передаёт раздел
/// «Записи» объектами маршрута: эндпоинта «получить запись по id» нет, а строка уже несёт
/// и визит, и пояс салона.
/// </para>
/// <para>
/// Успех переноса применяется к строке владельца здесь, а не во ViewModel экрана: только
/// раздел знает, как переставить время, баннер и бейдж «Ближайшая запись».
/// </para>
/// <para>
/// Сетка — чтение, поэтому автоповтор включается на всё время видимости экрана:
/// <see cref="PageVisibility.Watch"/> сообщает и уход приложения в фон, где
/// <c>OnDisappearing</c> не вызывается.
/// </para>
/// </remarks>
public partial class BarberBookingReschedulePage : ContentPage, IQueryAttributable
{
    private readonly BarberBookingRescheduleViewModel _viewModel;

    private BarberBookingItem? _item;
    private BarberBookingsViewModel? _owner;
    private bool _queryApplied;
    private bool _isAppeared;

    /// <summary>
    /// Область «экран виден»: из неё растёт автоповтор сетки, пока страница на виду.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт экран переноса.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public BarberBookingReschedulePage(BarberBookingRescheduleViewModel viewModel)
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
    /// как станет известно содержимое, поэтому единственный разумный выход — сразу закрыться.
    /// Если параметры пришли после первого появления, сетка читается здесь же: второго
    /// <c>OnAppearing</c> не будет.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("item", out var itemValue)
            || itemValue is not BarberBookingItem item
            || !query.TryGetValue("owner", out var ownerValue)
            || ownerValue is not BarberBookingsViewModel owner)
        {
            // Параметры уже разобраны: второй вызов не должен закрыть экран ещё раз.
            _queryApplied = true;

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
            _ = _viewModel.InitializeAsync(item);
        }
    }

    /// <summary>
    /// Включает автоповтор и читает сетку при первом появлении.
    /// </summary>
    /// <remarks>
    /// Область видимости создаётся до чтения: сбой, пришедший из него, уже может запустить
    /// цикл. <c>async void</c> — единственная допустимая форма переопределения, ошибки
    /// разбирает сам ViewModel.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _isAppeared = true;

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(
            this,
            _viewModel.StartAutoRetry,
            _viewModel.StopAutoRetry);

        if (_queryApplied && _item is { } item)
        {
            await _viewModel.InitializeAsync(item);
        }
    }

    /// <summary>
    /// Останавливает автоповтор, пока экран не виден.
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
    }
}
