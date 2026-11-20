using System.Globalization;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Views.Barber;

/// <summary>
/// Раздел «Салоны» кабинета мастера: привязанные места, основной салон и каталог.
/// </summary>
/// <remarks>
/// <para>
/// Раздел живёт внутри <c>BarberCabinetPage</c> и не имеет собственного жизненного цикла:
/// появление, уход и возврат с дочерних экранов ему сообщает страница.
/// </para>
/// <para>
/// Мобильные действия строки — звонок в салон, карта, копирование адреса — собраны здесь,
/// потому что Core не знает платформенных API: <see cref="BarberSalonItem"/> отдаёт
/// телефон, координаты и адрес, а «позвонить» или «открыть карту» — решение экрана.
/// Переход к расписанию этого салона поднимает событие: переключение разделов — дело
/// страницы кабинета, а не раздела.
/// </para>
/// <para>
/// Выбор строки каталога — единственное место, где кодовая часть пишет в состояние
/// ViewModel: у <see cref="BarberSalonsViewModel"/> нет отдельной команды выбора, а
/// <see cref="BarberSalonsViewModel.SelectedCatalogSalon"/> уже владеет этим состоянием
/// и пересчитывает доступность привязки.
/// </para>
/// </remarks>
public partial class BarberSalonsView : ContentView
{
    private readonly BarberSalonsViewModel _viewModel;

    /// <summary>
    /// Создаёт раздел и привязывает его к ViewModel.
    /// </summary>
    /// <param name="viewModel">Состояние раздела.</param>
    public BarberSalonsView(BarberSalonsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Состояние раздела: странице нужен он как общий контракт раздела кабинета.
    /// </summary>
    public BarberSalonsViewModel ViewModel => _viewModel;

    /// <summary>
    /// Просит страницу показать расписание выбранного салона.
    /// </summary>
    /// <remarks>
    /// Событие несёт идентификатор салона: раздел «Расписание» умеет выбрать салон
    /// отложенно, если его список ещё не прочитан.
    /// </remarks>
    public event EventHandler<Guid>? ScheduleRequested;

    /// <summary>
    /// Назначает салон основным: сервер сам снимает признак с прежнего.
    /// </summary>
    private async void OnSetPrimaryClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberSalonItem item })
        {
            await _viewModel.SetPrimaryCommand.ExecuteAsync(item);
        }
    }

    /// <summary>
    /// Звонит в салон: телефон строки собирается в <c>tel:</c> без пробелов и скобок.
    /// </summary>
    private async void OnCallClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberSalonItem item }
            || !item.HasPhoneNumber)
        {
            return;
        }

        var number = new string(
            item.PhoneNumber!
                .Where(character => char.IsDigit(character) || character == '+')
                .ToArray());

        if (number.Length == 0)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(new Uri(string.Concat("tel:", number)));
        }
        catch (Exception)
        {
            // Набор номера не открылся: экран остаётся как был, причина не блокирует работу.
        }
    }

    /// <summary>
    /// Открывает салон на карте: точка по координатам, иначе поиск по адресу.
    /// </summary>
    /// <remarks>
    /// <c>geo:</c> — схема Android; iOS её не знает, поэтому при отказе открывается
    /// веб-ссылка карт, которую понимают обе платформы. Неудача не показывается: карта —
    /// подсказка, а не данные раздела.
    /// </remarks>
    private async void OnMapClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberSalonItem item }
            || (!item.HasAddress && !item.HasCoordinates))
        {
            return;
        }

        var query = item.HasCoordinates
            ? string.Concat(
                item.Latitude.ToString("0.######", CultureInfo.InvariantCulture),
                ",",
                item.Longitude.ToString("0.######", CultureInfo.InvariantCulture))
            : item.FullAddress;

        var geo = item.HasCoordinates
            ? new Uri(
                string.Concat(
                    "geo:",
                    query,
                    "?q=",
                    query,
                    "(",
                    Uri.EscapeDataString(item.Name),
                    ")"))
            : new Uri(string.Concat("geo:0,0?q=", Uri.EscapeDataString(query)));

        try
        {
            if (await Launcher.Default.OpenAsync(geo))
            {
                return;
            }
        }
        catch (Exception)
        {
            // Схема geo: не зарегистрирована: пробуем ссылку ниже.
        }

        try
        {
            await Launcher.Default.OpenAsync(
                new Uri(
                    string.Concat(
                        "https://www.google.com/maps/search/?api=1&query=",
                        Uri.EscapeDataString(query))));
        }
        catch (Exception)
        {
            // Карта не открылась: экран остаётся как был.
        }
    }

    /// <summary>
    /// Копирует адрес салона в буфер обмена и объявляет результат баннером раздела.
    /// </summary>
    private async void OnCopyAddressClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberSalonItem item }
            || !item.HasAddress)
        {
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(item.FullAddress);

            _viewModel.Announcement = Loc.T("Адрес скопирован.");
        }
        catch (Exception)
        {
            // Буфер обмена недоступен: копирование — подсказка, а не данные раздела.
        }
    }

    /// <summary>
    /// Просит страницу открыть расписание этого салона.
    /// </summary>
    private void OnScheduleClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberSalonItem item })
        {
            ScheduleRequested?.Invoke(this, item.SalonId);
        }
    }

    /// <summary>
    /// Запоминает выбранную строку каталога: привязка доступна только после выбора.
    /// </summary>
    private void OnCatalogSalonChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value
            && sender is RadioButton { BindingContext: BarberSalonCatalogItem item })
        {
            _viewModel.SelectedCatalogSalon = item;
        }
    }
}
