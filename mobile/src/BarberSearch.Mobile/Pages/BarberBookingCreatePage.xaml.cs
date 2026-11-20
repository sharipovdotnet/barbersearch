using System.ComponentModel;
using BarberSearch.Mobile.Core.ViewModels;
using BarberSearch.Mobile.Core.ViewModels.Barber;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Записать клиента»: режим клиента, услуга, салон, дата, свободное время и комментарий.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута, поэтому ViewModel приходит конструктором.
/// <see cref="BarberBookingsViewModel"/> и необязательное время начала передаёт раздел
/// «Записи» объектами маршрута: форма «Занять» окно между визитами открывается с готовым
/// временем, а эндпоинта «получить запись по id» нет — строка ленты уже несёт всё нужное.
/// </para>
/// <para>
/// Услуги, салоны, сетка слотов и поиск клиентов — чтения, поэтому автоповтор включается на
/// всё время видимости экрана: <see cref="PageVisibility.Watch"/> сообщает и уход приложения
/// в фон, где <c>OnDisappearing</c> не вызывается. Отправка — запись, и молча не повторяется.
/// </para>
/// <para>
/// Успех применяется к списку владельца здесь, а не в ViewModel экрана: только раздел знает,
/// как обновить ленту и какой баннер показать.
/// </para>
/// </remarks>
public partial class BarberBookingCreatePage : ContentPage, IQueryAttributable
{
    private readonly BarberBookingCreateViewModel _viewModel;

    private BarberBookingsViewModel? _owner;
    private DateTimeOffset? _preferredStartAt;
    private bool _queryApplied;
    private bool _isAppeared;

    /// <summary>
    /// Область «экран виден»: из неё растут три цикла автоповтора чтений.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт форму записи клиента.
    /// </summary>
    /// <param name="viewModel">Состояние формы.</param>
    public BarberBookingCreatePage(BarberBookingCreateViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        // Подписи пунктов задаёт код: ItemDisplayBinding — привязка к элементу списка,
        // а не к ViewModel формы, поэтому компилированная разметка здесь не подходит.
        ServicePicker.ItemDisplayBinding = new Binding(nameof(BarberServiceItem.Name));
        SalonPicker.ItemDisplayBinding = new Binding(nameof(BarberSalonOption.DisplayText));

        UpdateModeSegments();
    }

    /// <summary>
    /// Принимает владельца списка и необязательное время начала.
    /// </summary>
    /// <remarks>
    /// Без владельца обновлять после успеха нечего: Shell создаёт страницу раньше, чем
    /// становится известно содержимое, поэтому единственный разумный выход — закрыться.
    /// Если параметры пришли после первого появления, форма читается здесь же: второго
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

        if (!query.TryGetValue("owner", out var ownerValue)
            || ownerValue is not BarberBookingsViewModel owner)
        {
            // Параметры уже разобраны: второй вызов не должен закрыть экран ещё раз.
            _queryApplied = true;

            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _owner = owner;

        if (query.TryGetValue("preferredStartAt", out var startValue)
            && startValue is DateTimeOffset startAt)
        {
            _preferredStartAt = startAt;
        }

        _queryApplied = true;

        _viewModel.BookingCreated += OnBookingCreated;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (_isAppeared)
        {
            _ = _viewModel.InitializeAsync(_preferredStartAt);
        }
    }

    /// <summary>
    /// Включает автоповтор и читает услуги и салоны при появлении экрана.
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

        if (_queryApplied)
        {
            await _viewModel.InitializeAsync(_preferredStartAt);
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
    /// Перекрашивает сегменты при смене режима.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BarberBookingCreateViewModel.Mode))
        {
            UpdateModeSegments();
        }
    }

    /// <summary>
    /// Красит выбранный режим стилем <c>SegmentButtonSelected</c>.
    /// </summary>
    private void UpdateModeSegments()
    {
        SegmentedButtonStyle.Apply(RegisteredButton, _viewModel.IsRegisteredMode);
        SegmentedButtonStyle.Apply(GuestButton, _viewModel.IsGuestMode);
    }

    /// <summary>
    /// Переключает на зарегистрированного клиента.
    /// </summary>
    private void OnRegisteredClicked(object? sender, EventArgs e) =>
        _viewModel.SelectRegisteredModeCommand.Execute(null);

    /// <summary>
    /// Переключает на нового клиента без аккаунта.
    /// </summary>
    private void OnGuestClicked(object? sender, EventArgs e) =>
        _viewModel.SelectGuestModeCommand.Execute(null);

    /// <summary>
    /// Применяет созданную запись к списку владельца и закрывает экран.
    /// </summary>
    /// <param name="sender">ViewModel формы записи.</param>
    /// <param name="result">Идентификатор брони и имя клиента.</param>
    private async void OnBookingCreated(object? sender, BarberBookingCreatedResult result)
    {
        var owner = _owner;

        if (owner is null)
        {
            return;
        }

        await owner.OnBookingCreated(result.ClientName);

        await Shell.Current.GoToAsync("..");
    }
}
