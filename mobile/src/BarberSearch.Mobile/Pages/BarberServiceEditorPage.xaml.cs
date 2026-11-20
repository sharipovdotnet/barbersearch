using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Услуга»: добавление из справочника или правка цены и длительности.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута. Профиль, владельца раздела и необязательную
/// строку прайса передаёт «Профиль» кабинета объектами маршрута: без <c>item</c> форма
/// добавляет услугу, с ним — правит существующую.
/// </para>
/// <para>
/// Скрытие и возврат услуги остаются в разделе: у строки прайса уже есть свои
/// «Скрыть»/«Вернуть», и второй путь к тому же действию здесь не нужен. Форма ничего
/// не читает, кроме ленивого справочника услуг, поэтому автоповтор здесь не заводится —
/// справочник повторяется кнопкой.
/// </para>
/// <para>
/// Сохранение — запись, и после успеха страница перечитывает прайс владельца
/// (<see cref="BarberCabinetProfileViewModel.ActivateAsync"/>) и закрывается: раздел
/// показывает новую строку, а возврат в кабинет снова активирует его.
/// </para>
/// </remarks>
public partial class BarberServiceEditorPage : ContentPage, IQueryAttributable
{
    private readonly BarberServiceEditorViewModel _viewModel;

    private BarberCabinetProfileViewModel? _owner;
    private bool _queryApplied;

    /// <summary>
    /// Создаёт редактор услуги.
    /// </summary>
    /// <param name="viewModel">Состояние формы.</param>
    public BarberServiceEditorPage(BarberServiceEditorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает профиль, владельца раздела и необязательную строку прайса.
    /// </summary>
    /// <remarks>
    /// Без профиля писать некуда, без владельца перечитывать прайс некому: Shell создаёт
    /// страницу раньше, чем становится известно содержимое, поэтому единственный разумный
    /// выход — сразу закрыться.
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
            || ownerValue is not BarberCabinetProfileViewModel owner
            || !query.TryGetValue("barberProfileId", out var profileValue)
            || profileValue is not Guid profileId)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _owner = owner;
        _queryApplied = true;

        if (query.TryGetValue("item", out var itemValue) && itemValue is BarberServiceItem item)
        {
            _viewModel.InitializeForEdit(profileId, item);
        }
        else
        {
            _viewModel.InitializeForAdd(profileId, owner.Services);
        }

        _viewModel.Saved += OnSaved;
    }

    /// <summary>
    /// Выбирает строку справочника: команды выбора у строки нет, а состояние выбора
    /// живёт в ViewModel.
    /// </summary>
    /// <param name="sender">Распознаватель нажатия строки.</param>
    /// <param name="e">Параметр строки — сама услуга справочника.</param>
    private void OnCatalogServiceTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is BarberServiceCatalogItem item)
        {
            _viewModel.SelectedCatalogService = item;
        }
    }

    /// <summary>
    /// Перечитывает прайс владельца и закрывает экран после успешного сохранения.
    /// </summary>
    /// <param name="sender">ViewModel редактора.</param>
    /// <param name="message">Подпись результата: «Услуга добавлена.» или «Цена обновлена.».</param>
    private async void OnSaved(object? sender, string message)
    {
        _ = message;

        if (_owner is { } owner)
        {
            await owner.ActivateAsync();
        }

        await Shell.Current.GoToAsync("..");
    }
}
