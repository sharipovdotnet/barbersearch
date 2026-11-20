using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Уведомления» кабинета: курсорный инбокс, «Прочитать все» и переход к записям.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута. Профиль мастера передаёт кабинет
/// параметром <c>barberProfileId</c>: у инбокса нет собственного <c>/me</c>, а счётчик
/// и список живут на профиле из сессии. Он же передаёт и себя (<c>owner</c>) — чтобы
/// инбокс мог попросить у него нужный раздел перед возвратом.
/// </para>
/// <para>
/// Чтения повторяются сами, пока страница видна: <see cref="PageVisibility.Watch"/> сообщает
/// и уход приложения в фон, где <c>OnDisappearing</c> не вызывается. Отметки прочтения —
/// записи, и молча не повторяются.
/// </para>
/// <para>
/// Строка ведёт в «Записи» кабинета и не может открыть конкретный визит: отдельного
/// <c>GET api/bookings/{id}</c> нет, а список уже содержит его. Поэтому перед закрытием
/// страница просит кабинет показать раздел с индексом 0 — кабинет применяет просьбу на
/// возврате, когда снова активирует раздел.
/// </para>
/// </remarks>
public partial class BarberNotificationsPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Индекс раздела «Записи» в списке разделов кабинета.
    /// </summary>
    private const int BookingsSectionIndex = 0;

    private readonly BarberNotificationsViewModel _viewModel;

    private BarberCabinetPage? _owner;
    private bool _queryApplied;

    /// <summary>
    /// Область «страница видна»: из неё растут два цикла автоповтора инбокса.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт экран уведомлений.
    /// </summary>
    /// <param name="viewModel">Состояние инбокса.</param>
    public BarberNotificationsPage(BarberNotificationsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        _viewModel.OpenRequested += OnOpenRequested;
    }

    /// <summary>
    /// Принимает профиль мастера и кабинет, который открыл инбокс.
    /// </summary>
    /// <remarks>
    /// Без профиля читать нечего: Shell создаёт страницу раньше, чем становится известно
    /// содержимое, поэтому единственный разумный выход — сразу закрыться. Кабинет
    /// необязателен: без него инбокс работает, просто возврат оставит прежний раздел.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("barberProfileId", out var profileValue)
            || profileValue is not Guid profileId
            || profileId == Guid.Empty)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _queryApplied = true;

        if (query.TryGetValue("owner", out var ownerValue) && ownerValue is BarberCabinetPage owner)
        {
            _owner = owner;
        }

        _viewModel.Initialize(profileId);
    }

    /// <summary>
    /// Включает автоповтор и читает первую страницу при появлении.
    /// </summary>
    /// <remarks>
    /// Область видимости создаётся до чтения: сбой, пришедший из него, уже может запустить
    /// цикл. <c>async void</c> — единственная допустимая форма переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(
            this,
            _viewModel.StartAutoRetry,
            _viewModel.StopAutoRetry);

        if (_queryApplied)
        {
            await _viewModel.ActivateAsync();
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
    /// Открывает строку инбокса: отметка прочтения и переход в «Записи».
    /// </summary>
    /// <param name="sender">Список инбокса.</param>
    /// <param name="e">Выбранная строка.</param>
    private async void OnNotificationSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not CollectionView list)
        {
            return;
        }

        var item = e.CurrentSelection.Count > 0 ? e.CurrentSelection[0] as BarberNotificationItem : null;

        // Выбор снимается сразу: строка — действие, а не состояние списка.
        list.SelectedItem = null;

        if (item is null)
        {
            return;
        }

        await _viewModel.OpenCommand.ExecuteAsync(item);
    }

    /// <summary>
    /// Просит кабинет показать «Записи» и закрывает инбокс.
    /// </summary>
    /// <param name="sender">ViewModel инбокса.</param>
    /// <param name="e">Событие без данных.</param>
    private async void OnOpenRequested(object? sender, EventArgs e)
    {
        _owner?.RequestSectionAfterPop(BookingsSectionIndex);

        await Shell.Current.GoToAsync("..");
    }
}
