using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Создать салон»: собственное место приёма барбера.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута, поэтому ViewModel приходит конструктором.
/// Раздел «Салоны» передаёт владельца объектом маршрута <c>owner</c>, а профиль мастера —
/// <c>barberProfileId</c>: без владельца объявлять результат некому, и страница закрывается.
/// </para>
/// <para>
/// Форма ничего не читает при открытии: подсказки города и адреса — это запросы на ввод,
/// а не загрузка экрана, поэтому автоповтор здесь не заводится. Отправка — запись, и она
/// молча не повторяется; после успеха страница пишет сообщение в баннер раздела и
/// закрывается. Список раздел перечитывает сам на Pop, второй вызов
/// <see cref="BarberSalonsViewModel.ActivateAsync"/> здесь был бы лишним запросом.
/// </para>
/// <para>
/// Возврат — единственная отмена: отдельной кнопки «Отмена» нет, как и у остальных
/// дочерних экранов кабинета.
/// </para>
/// </remarks>
public partial class BarberSalonCreatePage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут страницы в Shell.
    /// </summary>
    public const string Route = BarberCabinetRoutes.CreateSalon;

    private readonly BarberSalonCreateViewModel _viewModel;

    private BarberSalonsViewModel? _owner;
    private bool _queryApplied;

    /// <summary>
    /// Создаёт форму создания салона.
    /// </summary>
    /// <param name="viewModel">Состояние формы.</param>
    public BarberSalonCreatePage(BarberSalonCreateViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает раздел «Салоны» владельцем и профиль мастера.
    /// </summary>
    /// <remarks>
    /// Без владельца объявлять результат некому, а без профиля мастера создавать место
    /// некуда: Shell создаёт страницу раньше, чем становится известно содержимое, поэтому
    /// единственный разумный выход — сразу закрыться. Идентификатор профиля форме не нужен —
    /// сервер берёт его из токена, — но маршрут передаёт его рядом с владельцем для
    /// единообразия с остальными дочерними экранами кабинета.
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
            || ownerValue is not BarberSalonsViewModel { BarberProfileId: not null } owner)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _owner = owner;
        _queryApplied = true;

        _viewModel.Saved += OnSaved;
    }

    /// <summary>
    /// Запоминает тип места, выбранный радиокнопкой.
    /// </summary>
    /// <remarks>
    /// Пять вариантов рисуются <c>BindableLayout</c>, поэтому строка каталога веба повторяется
    /// здесь как карточка: у формы нет отдельной команды выбора, и выбор пишется в ViewModel
    /// из code-behind — так же, как выбранный салон в разделе «Салоны».
    /// </remarks>
    /// <param name="sender">Радиокнопка варианта.</param>
    /// <param name="e">Новое состояние радиокнопки.</param>
    private void OnKindChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value
            && sender is RadioButton { BindingContext: BarberSalonKindOption option })
        {
            _viewModel.SelectedKindOption = option;
        }
    }

    /// <summary>
    /// Выбирает тип места нажатием на всю карточку, а не только на кружок.
    /// </summary>
    /// <param name="sender">Распознаватель нажатия карточки.</param>
    /// <param name="e">Параметр карточки — сам вариант типа места.</param>
    private void OnKindTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is BarberSalonKindOption option)
        {
            _viewModel.SelectedKindOption = option;
        }
    }

    /// <summary>
    /// Подставляет город из выбранной подсказки и убирает клавиатуру.
    /// </summary>
    /// <param name="sender">Распознаватель нажатия строки.</param>
    /// <param name="e">Параметр строки — сама подсказка города.</param>
    private void OnCitySuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not CitySuggestionResponse suggestion)
        {
            return;
        }

        _viewModel.UseCitySuggestionCommand.Execute(suggestion);

        CityEntry.Unfocus();
    }

    /// <summary>
    /// Подставляет улицу и координаты из выбранной подсказки и убирает клавиатуру.
    /// </summary>
    /// <param name="sender">Распознаватель нажатия строки.</param>
    /// <param name="e">Параметр строки — сама подсказка адреса.</param>
    private void OnAddressSuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not AddressSuggestionResponse suggestion)
        {
            return;
        }

        _viewModel.UseAddressSuggestionCommand.Execute(suggestion);

        AddressEntry.Unfocus();
    }

    /// <summary>
    /// Объявляет результат в разделе «Салоны» и закрывает экран.
    /// </summary>
    /// <remarks>
    /// Список раздел перечитает сам на Pop; здесь только сообщение, чтобы человек увидел,
    /// ушло ли место на проверку или уже доступно для записи.
    /// </remarks>
    /// <param name="sender">ViewModel формы.</param>
    /// <param name="message">«Салон отправлен на проверку.» или «Место приёма создано.».</param>
    private async void OnSaved(object? sender, string message)
    {
        if (_owner is { } owner)
        {
            owner.Announcement = message;
        }

        await Shell.Current.GoToAsync("..");
    }
}
