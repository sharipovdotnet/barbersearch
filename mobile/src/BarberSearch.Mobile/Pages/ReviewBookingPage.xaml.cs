using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Отзыв о визите»: пять звёзд, комментарий и отправка.
/// </summary>
/// <remarks>
/// Страница резолвится из DI фабрикой маршрута, а строку списка и её ViewModel получает
/// объектами маршрута: эндпоинта «получить запись по id» нет. Успех отправки применяется к
/// строке владельца здесь же — только вкладка знает, как заменить кнопку на «Отзыв отправлен»
/// и показать баннер о модерации.
/// </remarks>
public partial class ReviewBookingPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "review";

    private readonly ReviewBookingViewModel _viewModel;

    private BookingListItem? _item;
    private BookingsViewModel? _owner;
    private bool _queryApplied;

    /// <summary>
    /// Создаёт экран отзыва.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public ReviewBookingPage(ReviewBookingViewModel viewModel)
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
    /// Без любого из двух параметров заполнять форму нечем: страница сразу закрывается,
    /// вместо того чтобы показать отзыв о неизвестно каком визите.
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
        _viewModel.Submitted += OnSubmitted;
    }

    /// <summary>
    /// Отмечает отзыв в строке владельца и закрывает экран.
    /// </summary>
    /// <remarks>
    /// Отзыв создаётся скрытым и уходит на модерацию: баннер об этом ставит ViewModel
    /// вкладки, а страница только закрывается.
    /// </remarks>
    /// <param name="sender">ViewModel экрана отзыва.</param>
    /// <param name="e">Признак успешной отправки.</param>
    private async void OnSubmitted(object? sender, EventArgs e)
    {
        var item = _item;
        var owner = _owner;

        if (item is null || owner is null)
        {
            return;
        }

        owner.ApplyReviewed(item);

        await Shell.Current.GoToAsync("..");
    }
}

/// <summary>
/// Оценки для <c>CommandParameter</c> звёзд.
/// </summary>
/// <remarks>
/// Литерал в XAML приходит строкой, а <c>SelectRatingCommand</c> принимает int; константы
/// дают типизированное значение без конвертера и без правки Core. Выбор звезды — это
/// свойство экрана, а не общий словарь, поэтому тип живёт рядом со страницей.
/// </remarks>
public static class ReviewRating
{
    /// <summary>
    /// Одна звезда.
    /// </summary>
    public const int One = 1;

    /// <summary>
    /// Две звезды.
    /// </summary>
    public const int Two = 2;

    /// <summary>
    /// Три звезды.
    /// </summary>
    public const int Three = 3;

    /// <summary>
    /// Четыре звезды.
    /// </summary>
    public const int Four = 4;

    /// <summary>
    /// Пять звёзд.
    /// </summary>
    public const int Five = 5;
}
