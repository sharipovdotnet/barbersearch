using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Добавить салон»: поиск по публичному каталогу и привязка к профилю мастера.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута, поэтому ViewModel приходит конструктором.
/// Раздел «Салоны» передаёт владельца объектом маршрута <c>owner</c>: без владельца
/// объявлять результат некому, и страница закрывается. Профиль мастера и признак «есть
/// салоны» берутся у владельца — отдельный параметр маршрута не нужен.
/// </para>
/// <para>
/// Поиск каталога откладывается и повторяется сам, поэтому видимость страницы важна так
/// же, как у раздела: <see cref="PageVisibility"/> останавливает цикл, когда приложение
/// уходит в фон, и возвращает его при появлении. Список «Мои салоны» остаётся в скрытом
/// разделе и перечитывается на Pop, поэтому страница не зовёт его методы.
/// </para>
/// <para>
/// Возврат — единственная отмена: отдельной кнопки «Отмена» нет, как и у остальных
/// дочерних экранов кабинета.
/// </para>
/// </remarks>
public partial class BarberSalonAddPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут страницы в Shell.
    /// </summary>
    public const string Route = BarberCabinetRoutes.AddSalon;

    private readonly BarberSalonAddViewModel _viewModel;

    private BarberSalonsViewModel? _owner;
    private IDisposable? _visibility;
    private bool _queryApplied;

    /// <summary>
    /// Создаёт форму добавления салона.
    /// </summary>
    /// <param name="viewModel">Состояние формы.</param>
    public BarberSalonAddPage(BarberSalonAddViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает раздел «Салоны» владельцем и инициализирует форму его профилем.
    /// </summary>
    /// <remarks>
    /// Без владельца объявлять результат некому, а без профиля мастера привязывать салон
    /// некуда: Shell создаёт страницу раньше, чем становится известно содержимое, поэтому
    /// единственный разумный выход — сразу закрыться. Инициализация одна на страницу:
    /// повторный вызов не должен сбрасывать уже набранный запрос.
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
            || ownerValue is not BarberSalonsViewModel { BarberProfileId: { } profileId } owner)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _owner = owner;
        _queryApplied = true;

        _viewModel.Initialize(profileId, owner.HasSalons);
        _viewModel.Attached += OnAttached;
    }

    /// <summary>
    /// Подписывается на видимость страницы: каталог повторяет упавший запрос сам.
    /// </summary>
    /// <remarks>
    /// Подписка не ждёт <see cref="ApplyQueryAttributes"/>: Shell не гарантирует, что
    /// параметры маршрута придут раньше появления, а окно нужно слушать с первого кадра.
    /// Неинициализированная ViewModel просто не начинает поиск.
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        _visibility ??= PageVisibility.Watch(
            this,
            _viewModel.StartAutoRetry,
            _viewModel.StopAutoRetry);
    }

    /// <summary>
    /// Снимает подписку на видимость: цикл останавливается вместе со страницей.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _visibility?.Dispose();
        _visibility = null;
    }

    /// <summary>
    /// Запоминает выбранную строку каталога: привязка доступна только после выбора.
    /// </summary>
    /// <param name="sender">Радиокнопка строки каталога.</param>
    /// <param name="e">Новое состояние радиокнопки.</param>
    private void OnCatalogSalonChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value
            && sender is RadioButton { BindingContext: BarberSalonCatalogItem item })
        {
            _viewModel.SelectedCatalogSalon = item;
        }
    }

    /// <summary>
    /// Выбирает строку каталога нажатием на всю карточку, а не только на кружок.
    /// </summary>
    /// <param name="sender">Распознаватель нажатия карточки.</param>
    /// <param name="e">Параметр карточки — сама строка каталога.</param>
    private void OnCatalogSalonTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is BarberSalonCatalogItem item)
        {
            _viewModel.SelectedCatalogSalon = item;
        }
    }

    /// <summary>
    /// Объявляет результат в разделе «Салоны» и закрывает экран.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Список раздел перечитает сам на Pop. Устаревшая привязка сюда не доходит: она
    /// показывает причину и оставляет страницу открытой, чтобы выбрать другую строку.
    /// </para>
    /// <para>
    /// Если человек уже ушёл с экрана, пока запрос был в полёте, закрывать нечего: сообщение
    /// всё равно пишется в раздел, а второй <c>Pop</c> снял бы сам кабинет.
    /// </para>
    /// </remarks>
    /// <param name="sender">ViewModel формы.</param>
    /// <param name="message">«Салон добавлен.».</param>
    private async void OnAttached(object? sender, string message)
    {
        _viewModel.Attached -= OnAttached;

        if (_owner is { } owner)
        {
            owner.Announcement = message;
        }

        if (!ReferenceEquals(Shell.Current.CurrentPage, this))
        {
            return;
        }

        await Shell.Current.GoToAsync("..");
    }
}
