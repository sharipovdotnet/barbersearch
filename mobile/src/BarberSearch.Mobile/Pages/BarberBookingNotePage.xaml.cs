using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Заметка к записи»: текст до тысячи символов и сохранение.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута, поэтому ViewModel приходит конструктором.
/// Строку записи и её <see cref="BarberBookingsViewModel"/> передаёт раздел «Записи»
/// объектами маршрута: отдельного <c>GET api/bookings/{id}</c> нет, а строка уже несёт
/// заметку, имя клиента и момент визита.
/// </para>
/// <para>
/// Сохранение применяется к строке владельца здесь, а не в ViewModel экрана: только раздел
/// знает, как переставить текст в карточке и какой баннер показать. Чтений у экрана нет,
/// поэтому <c>PageVisibility</c> и автоповтор здесь не нужны — записи не повторяются молча.
/// </para>
/// </remarks>
public partial class BarberBookingNotePage : ContentPage, IQueryAttributable
{
    private readonly BarberBookingNoteViewModel _viewModel;

    private BarberBookingItem? _item;
    private BarberBookingsViewModel? _owner;
    private bool _queryApplied;

    /// <summary>
    /// Создаёт экран заметки.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public BarberBookingNotePage(BarberBookingNoteViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает строку записи и её ViewModel.
    /// </summary>
    /// <remarks>
    /// Без любого из двух параметров открывать нечего: Shell создаёт страницу раньше, чем
    /// становится известно содержимое, поэтому единственный разумный выход — сразу закрыться.
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
        _viewModel.Saved += OnSaved;
    }

    /// <summary>
    /// Применяет сохранённый текст к строке владельца и закрывает экран.
    /// </summary>
    /// <remarks>
    /// Событие приходит уже с обрезанным текстом — ровно тем, что ушёл на сервер, — поэтому
    /// страница ничего не решает и не повторяет запрос.
    /// </remarks>
    /// <param name="sender">ViewModel экрана заметки.</param>
    /// <param name="note">Текст, который подтвердил сервер.</param>
    private async void OnSaved(object? sender, string note)
    {
        var item = _item;
        var owner = _owner;

        if (item is null || owner is null)
        {
            return;
        }

        owner.ApplyNote(item, note);

        await Shell.Current.GoToAsync("..");
    }
}
