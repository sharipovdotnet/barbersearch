using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Barber;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile.Views.Barber;

/// <summary>
/// Раздел «Записи» кабинета: лента дня, списки «Ближайшие» и «История» и действия над визитом.
/// </summary>
/// <remarks>
/// <para>
/// Раздел — не страница: он живёт внутри <c>BarberCabinetPage</c>, и жизненный цикл ему
/// сообщает страница. Поэтому перед каждым переходом на дочерний экран раздел сам гасит
/// ViewModel (<c>Deactivate</c>): Shell не поднимает <c>OnDisappearing</c> при push, и
/// автоповтор иначе продолжал бы ходить в сеть под чужой страницей.
/// </para>
/// <para>
/// Действия вызывают методы ViewModel напрямую, а не команды: у методов есть параметр
/// строки, а <c>DisplayPrompt</c> «какой причины» — это платформенный диалог и работа
/// экрана. Ошибки действий ViewModel кладёт в саму строку, поэтому второго места для
/// них здесь нет.
/// </para>
/// <para>
/// Пока открыт диалог причины отмены или неявки, страница не запускает тихое обновление:
/// <see cref="IsPromptOpen"/> читает таймер кабинета. Между закрытием диалога и мутацией
/// нет <c>await</c>, поэтому обновление не может встать между ними.
/// </para>
/// </remarks>
public partial class BarberBookingsView : ContentView
{
    /// <summary>
    /// Предел причины отмены на сервере; клиент повторяет его в счётчике и в диалоге.
    /// </summary>
    private const int CancellationReasonMaxLength = 500;

    private readonly BarberBookingsViewModel _viewModel;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Создаёт раздел и подписывается на события ViewModel.
    /// </summary>
    /// <param name="viewModel">Состояние раздела.</param>
    /// <param name="timeProvider">Часы: «следующий клиент» считается по ним.</param>
    public BarberBookingsView(BarberBookingsViewModel viewModel, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(timeProvider);

        InitializeComponent();

        _viewModel = viewModel;
        _timeProvider = timeProvider;

        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Items.CollectionChanged += OnItemsChanged;
        _viewModel.CreateBookingRequested += OnCreateBookingRequested;
        _viewModel.FillGapRequested += OnFillGapRequested;

        UpdateSegments();
    }

    /// <summary>
    /// Состояние раздела: странице нужен он для таймера и для общего контракта раздела.
    /// </summary>
    public BarberBookingsViewModel ViewModel => _viewModel;

    /// <summary>
    /// На экране диалог причины отмены или подтверждение неявки.
    /// </summary>
    /// <remarks>
    /// Читает таймер страницы: пока диалог открыт, тихое обновление списка не запускается,
    /// иначе строка под руками могла бы сменить статус из-за ответа, прочитанного до мутации.
    /// </remarks>
    public bool IsPromptOpen { get; private set; }

    /// <summary>
    /// Вид списка сменился: перекрашиваются сегменты и пересчитывается «следующий клиент».
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BarberBookingsViewModel.View))
        {
            return;
        }

        UpdateSegments();
        UpdateNextClient();
    }

    /// <summary>
    /// Строки пересобраны: первый будущий визит дня получает бейдж «Следующий клиент».
    /// </summary>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateNextClient();

    /// <summary>
    /// Красит выбранный вид записей стилем <c>SegmentButtonSelected</c>.
    /// </summary>
    private void UpdateSegments()
    {
        SegmentedButtonStyle.Apply(TodayButton, _viewModel.IsToday);
        SegmentedButtonStyle.Apply(UpcomingButton, _viewModel.IsUpcoming);
        SegmentedButtonStyle.Apply(PastButton, _viewModel.IsPast);
    }

    /// <summary>
    /// Ставит бейдж «Следующий клиент» первой будущей строке ленты дня.
    /// </summary>
    /// <remarks>
    /// Признак — принадлежность ленте «Сегодня»: в «Ближайших» и «Истории» бейджа нет,
    /// а отменённая запись следующим клиентом не считается. Строки у видов общие, поэтому
    /// признак снимается со всех, а не только с прошлой отмеченной.
    /// </remarks>
    private void UpdateNextClient()
    {
        var now = _timeProvider.GetUtcNow();
        BarberBookingItem? next = null;

        if (_viewModel.IsToday)
        {
            foreach (var item in _viewModel.Items)
            {
                if (!item.IsGap
                    && item.EndAt > now
                    && item.Status is BookingStatus.Pending or BookingStatus.Confirmed)
                {
                    next = item;

                    break;
                }
            }
        }

        foreach (var item in _viewModel.Items)
        {
            item.IsNextClient = ReferenceEquals(item, next);
        }
    }

    /// <summary>
    /// Переключает на «Сегодня».
    /// </summary>
    private async void OnTodayClicked(object? sender, EventArgs e) =>
        await _viewModel.SelectTodayCommand.ExecuteAsync(null);

    /// <summary>
    /// Переключает на «Ближайшие».
    /// </summary>
    private async void OnUpcomingClicked(object? sender, EventArgs e) =>
        await _viewModel.SelectUpcomingCommand.ExecuteAsync(null);

    /// <summary>
    /// Переключает на «Историю».
    /// </summary>
    private async void OnPastClicked(object? sender, EventArgs e) =>
        await _viewModel.SelectPastCommand.ExecuteAsync(null);

    /// <summary>
    /// Подтверждает запись: сервер отвечает <c>204</c>, строка меняет статус на месте.
    /// </summary>
    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberBookingItem item })
        {
            await _viewModel.ConfirmAsync(item);
        }
    }

    /// <summary>
    /// Завершает визит: строка уходит из «Ближайших» в историю.
    /// </summary>
    private async void OnCompleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberBookingItem item })
        {
            await _viewModel.CompleteAsync(item);
        }
    }

    /// <summary>
    /// Отмечает неявку после подтверждения: действие необратимо.
    /// </summary>
    private async void OnNoShowClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberBookingItem item })
        {
            return;
        }

        bool accepted;

        try
        {
            IsPromptOpen = true;

            accepted = await ConfirmAsync(
                Loc.T("Отметить неявку?"),
                Loc.T("Запись закроется со статусом «Неявка». Вернуть её или завершить визит после этого нельзя."),
                Loc.T("Отметить неявку"),
                Loc.T("Не отмечать"));
        }
        finally
        {
            IsPromptOpen = false;
        }

        if (!accepted)
        {
            return;
        }

        await _viewModel.MarkNoShowAsync(item);
    }

    /// <summary>
    /// Отменяет запись: причина обязательна и ограничена пятьюстами символами.
    /// </summary>
    /// <remarks>
    /// Механизм тот же, что у клиентской вкладки «Записи»: нативные диалоги платформы.
    /// Пустая причина не отправляется — диалог открывается снова с объяснением, а отмена
    /// самого диалога означает «не отменять запись».
    /// </remarks>
    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberBookingItem item })
        {
            return;
        }

        var page = DialogPage;

        if (page is null)
        {
            return;
        }

        try
        {
            IsPromptOpen = true;

            while (true)
            {
                var answer = await page.DisplayPromptAsync(
                    Loc.T("Отменить запись?"),
                    Loc.T("Укажите причину отмены. Отмена необратима: слот освободится, и время сможет занять другой клиент."),
                    Loc.T("Отменить запись"),
                    Loc.T("Не отменять"),
                    Loc.T("Например: мастер заболел"),
                    CancellationReasonMaxLength,
                    Keyboard.Text);

                if (answer is null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(answer))
                {
                    await _viewModel.CancelAsync(item, answer.Trim());

                    return;
                }

                await page.DisplayAlertAsync(
                    Loc.T("Причина отмены"),
                    Loc.T("Укажите причину отмены."),
                    Loc.T("Понятно"));
            }
        }
        finally
        {
            IsPromptOpen = false;
        }
    }

    /// <summary>
    /// Меняет цену визита: сумма вводится числом, проверяется локально.
    /// </summary>
    private async void OnPriceClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberBookingItem item })
        {
            return;
        }

        var page = DialogPage;

        if (page is null)
        {
            return;
        }

        try
        {
            IsPromptOpen = true;

            while (true)
            {
                var initial = item.Price.ToString("0.##", Loc.Culture);

                var answer = await page.DisplayPromptAsync(
                    Loc.T("Цена визита"),
                    Loc.T("Новая цена визита."),
                    Loc.T("Сохранить"),
                    Loc.T("Отмена"),
                    null,
                    32,
                    Keyboard.Numeric,
                    initial);

                if (answer is null)
                {
                    return;
                }

                if (TryParsePrice(answer, out var price))
                {
                    await _viewModel.ChangePriceAsync(item, price);

                    return;
                }

                await page.DisplayAlertAsync(
                    Loc.T("Цена визита"),
                    Loc.T("Укажите цену числом."),
                    Loc.T("Понятно"));
            }
        }
        finally
        {
            IsPromptOpen = false;
        }
    }

    /// <summary>
    /// Открывает экран заметки: параметры <c>item</c> и <c>owner</c> — как в §5 среза.
    /// </summary>
    private async void OnNoteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberBookingItem item })
        {
            return;
        }

        _viewModel.Deactivate();

        await Shell.Current.GoToAsync(
            BarberCabinetRoutes.BookingNote,
            new Dictionary<string, object>
            {
                ["item"] = item,
                ["owner"] = _viewModel,
            });
    }

    /// <summary>
    /// Открывает экран переноса: параметры <c>item</c> и <c>owner</c>.
    /// </summary>
    private async void OnRescheduleClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberBookingItem item })
        {
            return;
        }

        _viewModel.Deactivate();

        await Shell.Current.GoToAsync(
            BarberCabinetRoutes.RescheduleBooking,
            new Dictionary<string, object>
            {
                ["item"] = item,
                ["owner"] = _viewModel,
            });
    }

    /// <summary>
    /// Занимает окно между визитами: та же форма записи, но с готовым временем начала.
    /// </summary>
    private void OnFillGapClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberBookingItem item } && item.IsGap)
        {
            _viewModel.FillGapCommand.Execute(item);
        }
    }

    /// <summary>
    /// Перечитывает список после <c>409</c>: строка разошлась с сервером.
    /// </summary>
    private async void OnReloadRowClicked(object? sender, EventArgs e) =>
        await _viewModel.ReloadAsync();

    /// <summary>
    /// Звонит клиенту: телефон в строке — единственное место, где PII становится ссылкой.
    /// </summary>
    private async void OnPhoneClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberBookingItem item } || !item.HasPhone)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(item.PhoneUri);
        }
        catch (Exception)
        {
            // Набор номера не открылся: экран остаётся как был, причина не блокирует работу.
        }
    }

    /// <summary>
    /// Мастер нажал «Записать клиента».
    /// </summary>
    private async void OnCreateBookingRequested(object? sender, EventArgs e) =>
        await OpenCreateAsync(startAt: null);

    /// <summary>
    /// Мастер нажал «Занять» в окне: форма откроется с этим временем.
    /// </summary>
    private async void OnFillGapRequested(object? sender, BarberGapRequest request) =>
        await OpenCreateAsync(request.StartAt);

    /// <summary>
    /// Открывает форму записи клиента с параметром <c>owner</c> и, если есть, временем начала.
    /// </summary>
    private async Task OpenCreateAsync(DateTimeOffset? startAt)
    {
        _viewModel.Deactivate();

        var parameters = new Dictionary<string, object> { ["owner"] = _viewModel };

        if (startAt is { } moment)
        {
            parameters["preferredStartAt"] = moment;
        }

        await Shell.Current.GoToAsync(BarberCabinetRoutes.CreateBooking, parameters);
    }

    /// <summary>
    /// Разбирает цену в языке интерфейса: «2 400,50» и «2400.50» равны.
    /// </summary>
    private static bool TryParsePrice(string text, out decimal price)
    {
        var cleaned = text
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace("\u202F", string.Empty, StringComparison.Ordinal);

        return decimal.TryParse(
            cleaned,
            NumberStyles.Number,
            Loc.Culture,
            out price) && price >= 0m;
    }

    /// <summary>
    /// Страница, на которой показывается диалог: активная страница Shell.
    /// </summary>
    private static Page? DialogPage =>
        Shell.Current?.CurrentPage ?? (Page?)Shell.Current;

    /// <summary>
    /// Показывает нативное подтверждение и возвращает выбор.
    /// </summary>
    private static async Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        string cancel)
    {
        var page = DialogPage;

        return page is not null && await page.DisplayAlertAsync(title, message, accept, cancel);
    }
}
