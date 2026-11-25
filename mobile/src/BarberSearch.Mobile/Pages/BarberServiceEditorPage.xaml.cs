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
/// <para>
/// Выбор строки справочника отвечает движением, а не только цветом: строка на мгновение
/// сжимается под пальцем, карточка цены выезжает на первом выборе, и страница уводит к ней —
/// иначе подсвеченная строка осталась бы внизу, а поля — выше, за пределами экрана.
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
            _viewModel.InitializeForAdd(
                profileId,
                owner.Services,
                owner.Specializations
                    .Select(specialization => specialization.SpecializationId)
                    .ToArray());
        }

        _viewModel.Saved += OnSaved;
    }

    /// <summary>
    /// Выбирает строку справочника: команды выбора у строки нет, а состояние выбора
    /// живёт в ViewModel.
    /// </summary>
    /// <remarks>
    /// Движения идут вместе: прокрутка к карточке, отклик строки и (на первом выборе) выезд
    /// карточки. Последовательное выполнение читалось бы как задержка между касанием и ответом.
    /// </remarks>
    /// <param name="sender">Строка справочника, к которой привязан жест.</param>
    /// <param name="e">Параметр строки — сама услуга справочника.</param>
    private async void OnCatalogServiceTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BarberServiceCatalogItem item)
        {
            return;
        }

        var isCardShown = _viewModel.IsFormVisible;

        _viewModel.SelectedCatalogService = item;

        // Цена и длительность стоят над списком: после выбора экран уводит к ним.
        var scroll = FormScroll.ScrollToAsync(0, 0, true);

        // Отклик на касание: строка «проседает» под пальцем и возвращается. Движение видно и
        // тогда, когда выбор не изменился, — повторное нажатие тоже должно отвечать.
        var pulse = sender is VisualElement row
            ? PulseAsync(row)
            : Task.CompletedTask;

        // Карточка раскрывается на первом выборе: дальше у неё меняется только содержимое.
        var reveal = !isCardShown && _viewModel.IsFormVisible
            ? RevealEditorCardAsync()
            : Task.CompletedTask;

        await Task.WhenAll(pulse, reveal);
        await scroll;
    }

    /// <summary>
    /// Коротко сжимает строку под пальцем и отпускает.
    /// </summary>
    /// <param name="row">Строка справочника.</param>
    /// <returns>Задача анимации.</returns>
    private static async Task PulseAsync(VisualElement row)
    {
        await row.ScaleToAsync(0.97, 60, Easing.CubicOut);
        await row.ScaleToAsync(1, 120, Easing.CubicOut);
    }

    /// <summary>
    /// Показывает карточку цены с коротким выездом: выбор услуги должен читаться как шаг,
    /// а не как мгновенная подмена содержимого.
    /// </summary>
    /// <returns>Задача анимации.</returns>
    private async Task RevealEditorCardAsync()
    {
        EditorCard.Opacity = 0;
        EditorCard.TranslationY = 8;

        await Task.WhenAll(
            EditorCard.FadeToAsync(1, 180, Easing.CubicOut),
            EditorCard.TranslateToAsync(0, 0, 200, Easing.CubicOut));
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
