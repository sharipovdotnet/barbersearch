using System.ComponentModel;
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
/// <para>
/// Свёрнутая группа — это заголовок с шевроном: состояние живёт в редакторе, а страница только
/// ведёт поворот стрелки. Подписка на изменение <see cref="BarberServiceCatalogGroup.IsExpanded"/>
/// вместо поворота по касанию нужна потому, что группу открывает ещё и поиск, и выбор услуги:
/// стрелка обязана смотреть вниз всегда, когда строки видны.
/// </para>
/// </remarks>
public partial class BarberServiceEditorPage : ContentPage, IQueryAttributable
{
    private readonly BarberServiceEditorViewModel _viewModel;

    /// <summary>
    /// Шевроны групп: по одному на отрисованный заголовок, с подпиской на раскрытие своей группы.
    /// </summary>
    /// <remarks>
    /// Словарь, а не поиск по визуальному дереву: шаблон группы создаёт стрелку сам, и её
    /// ссылка нужна только затем, чтобы анимировать поворот. Группы живут столько же, сколько
    /// страница, поэтому подписки снимаются вместе с ней.
    /// </remarks>
    private readonly Dictionary<Label, (BarberServiceCatalogGroup Group, PropertyChangedEventHandler Handler)>
        _chevrons = [];

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
    /// Сворачивает или раскрывает группу по нажатию на её заголовок.
    /// </summary>
    /// <remarks>
    /// Решение принимает редактор: у него в одной команде и флаг группы, и правило «чужие
    /// группы не трогать». Страница только передаёт нажатую группу.
    /// </remarks>
    /// <param name="sender">Строка заголовка группы.</param>
    /// <param name="e">Параметр строки — сама группа.</param>
    private void OnCatalogGroupTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is BarberServiceCatalogGroup group)
        {
            _viewModel.ToggleGroupCommand.Execute(group);
        }
    }

    /// <summary>
    /// Подписывает шеврон на раскрытие своей группы и ставит стрелку по текущему состоянию.
    /// </summary>
    /// <remarks>
    /// Событие приходит, когда шаблон группы выдал стрелке контекст, — раньше подписываться
    /// не на что, а нарисованная стрелка должна сразу смотреть в свою сторону: раскрытие по
    /// умолчанию задаётся до того, как список отрисован.
    /// </remarks>
    /// <param name="sender">Шеврон группы.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnGroupChevronBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not Label chevron)
        {
            return;
        }

        if (_chevrons.Remove(chevron, out var previous))
        {
            previous.Group.PropertyChanged -= previous.Handler;
        }

        if (chevron.BindingContext is not BarberServiceCatalogGroup group)
        {
            return;
        }

        chevron.Rotation = ChevronRotation(group.IsExpanded);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(BarberServiceCatalogGroup.IsExpanded))
            {
                AnimateChevron(chevron, group.IsExpanded);
            }
        };

        group.PropertyChanged += handler;
        _chevrons[chevron] = (group, handler);
    }

    /// <summary>
    /// Поворот стрелки по состоянию группы.
    /// </summary>
    /// <param name="isExpanded">Группа раскрыта.</param>
    /// <returns>Угол в градусах: 90 — вниз, 0 — вправо.</returns>
    private static double ChevronRotation(bool isExpanded) => isExpanded ? 90 : 0;

    /// <summary>
    /// Поворачивает шеврон к новому состоянию группы.
    /// </summary>
    /// <remarks>
    /// Анимация — единственное, что страница делает с шевроном: высоту строк не анимируем,
    /// потому что дешёвой анимации высоты в MAUI нет, а твин <c>HeightRequest</c> дерётся с
    /// разметкой. Ошибку глотаем: страницу могли закрыть, пока стрелка ехала.
    /// </remarks>
    /// <param name="chevron">Шеврон группы.</param>
    /// <param name="isExpanded">Группа раскрыта.</param>
    private static async void AnimateChevron(Label chevron, bool isExpanded)
    {
        try
        {
            await chevron.RotateToAsync(ChevronRotation(isExpanded), 160, Easing.CubicOut);
        }
        catch (Exception)
        {
            // Страница закрылась во время анимации: показывать нечего.
        }
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
