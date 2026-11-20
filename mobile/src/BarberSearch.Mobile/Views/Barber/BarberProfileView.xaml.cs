using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Legal;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Barber;
using BarberSearch.Mobile.Pages;

namespace BarberSearch.Mobile.Views.Barber;

/// <summary>
/// Раздел «Профиль» кабинета мастера: карточка, фотографии, прайс и специализации.
/// </summary>
/// <remarks>
/// <para>
/// Раздел живёт внутри <c>BarberCabinetPage</c> и не имеет собственного жизненного цикла:
/// появление, уход и возврат с дочерних экранов ему сообщает страница. Правка карточки и
/// услуги — отдельные зарегистрированные маршруты (<see cref="BarberCabinetRoutes"/>):
/// раздел передаёт им параметры, а страницы и переходы создаёт Shell.
/// </para>
/// <para>
/// Адрес картинки — дело экрана: Core не знает базового адреса API. Поэтому сетка
/// строится здесь из <see cref="BarberProfilePhotoItem"/> через <see cref="MediaUrl"/>,
/// а готовые плитки лежат в локальной коллекции, а не в ViewModel: второго состояния
/// карточки не появляется. Сами действия над кадром живут на отдельном экране
/// (<see cref="BarberPhotoPage"/>): раздел передаёт ему снимок списка и переходит.
/// </para>
/// <para>
/// Выбор специализации в пикере — единственное локальное состояние: у ViewModel нет
/// свойства выбора, а команда добавления принимает идентификатор. Список специализаций
/// перечитывается вместе с карточкой, поэтому выбор сбрасывается и здесь.
/// </para>
/// </remarks>
public partial class BarberProfileView : ContentView
{
    /// <summary>
    /// Промежуток между плитками фотографий: gap-3 сетки кабинета веба, 12.
    /// </summary>
    private const double PhotoTileGap = 12;

    private readonly BarberCabinetProfileViewModel _viewModel;
    private readonly BarberSearchApiOptions _apiOptions;
    private readonly IAdminMediaApi _mediaApi;
    private readonly LegalOptions _legalOptions;
    private readonly ObservableCollection<BarberProfilePhotoTile> _photoTiles = [];

    private Guid? _selectedSpecializationId;
    private bool _isNavigating;
    private double _photoTileWidth;
    private int _avatarGeneration;

    /// <summary>
    /// Создаёт раздел, подписывается на события ViewModel и строит сетку фотографий.
    /// </summary>
    /// <param name="viewModel">Состояние раздела.</param>
    /// <param name="apiOptions">Базовый адрес API: относительные адреса фото склеиваются им.</param>
    /// <param name="mediaApi">Чтение неодобренных кадров с Bearer: анониму раздача отвечает <c>404</c>.</param>
    /// <param name="legalOptions">Базовый адрес сайта: из него собирается ссылка для «Поделиться».</param>
    public BarberProfileView(
        BarberCabinetProfileViewModel viewModel,
        BarberSearchApiOptions apiOptions,
        IAdminMediaApi mediaApi,
        LegalOptions legalOptions)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(apiOptions);
        ArgumentNullException.ThrowIfNull(mediaApi);
        ArgumentNullException.ThrowIfNull(legalOptions);

        InitializeComponent();

        _viewModel = viewModel;
        _apiOptions = apiOptions;
        _mediaApi = mediaApi;
        _legalOptions = legalOptions;

        BindingContext = viewModel;

        BindableLayout.SetItemsSource(PhotoTilesHost, _photoTiles);
        PhotoTilesHost.SizeChanged += OnPhotoTilesHostSizeChanged;

        // Подпись строки задаётся кодом: ItemDisplayBinding — привязка к элементу, а не к
        // ViewModel раздела, поэтому компилированная разметка здесь не подходит.
        SpecializationPicker.ItemDisplayBinding = new Binding(
            nameof(BarberSpecializationOption.Name));

        _viewModel.Photos.CollectionChanged += OnPhotosChanged;
        _viewModel.OpenEditRequested += OnOpenEditRequested;
        _viewModel.AddServiceRequested += OnAddServiceRequested;
        _viewModel.EditServiceRequested += OnEditServiceRequested;
        _viewModel.PreviewRequested += OnPreviewRequested;
        _viewModel.ShareProfileRequested += OnShareProfileRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        RebuildPhotoTiles();

        // Карточку могли прочитать до создания раздела: событие об изменении тогда уже
        // не придёт, а адрес аватара в ViewModel уже есть.
        _ = RefreshAvatarImageAsync();
    }

    /// <summary>
    /// Состояние раздела: странице нужен он как общий контракт раздела кабинета.
    /// </summary>
    public BarberCabinetProfileViewModel ViewModel => _viewModel;

    /// <summary>
    /// Возврат на форму входа: у раздела нет собственной формы.
    /// </summary>
    private async void OnSignInClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//profile");

    /// <summary>
    /// Открывает правку карточки: форма получит карточку и владельца раздела.
    /// </summary>
    /// <remarks>
    /// Перед переходом раздел гасится: Shell не поднимает <c>OnDisappearing</c> у страницы,
    /// поверх которой открыли следующую, и автоповтор иначе продолжил бы ходить в сеть.
    /// </remarks>
    private async void OnOpenEditRequested(object? sender, EventArgs e)
    {
        if (_viewModel.Profile is not { } profile)
        {
            return;
        }

        _viewModel.Deactivate();

        await Shell.Current.GoToAsync(
            BarberCabinetRoutes.EditProfile,
            new Dictionary<string, object>
            {
                ["owner"] = _viewModel,
                ["profile"] = profile,
            });
    }

    /// <summary>
    /// Открывает добавление услуги: редактору нужен профиль и текущий прайс.
    /// </summary>
    private async void OnAddServiceRequested(object? sender, EventArgs e) =>
        await OpenServiceEditorAsync(item: null);

    /// <summary>
    /// Открывает правку выбранной услуги.
    /// </summary>
    private async void OnEditServiceRequested(object? sender, BarberServiceItem item) =>
        await OpenServiceEditorAsync(item);

    /// <summary>
    /// Переход в редактор услуги: параметры повторяют то, что готовит ViewModel редактора.
    /// </summary>
    /// <param name="item">Строка прайса; <c>null</c> — добавление.</param>
    /// <returns>Задача перехода.</returns>
    private async Task OpenServiceEditorAsync(BarberServiceItem? item)
    {
        var profileId = _viewModel.BarberProfileId;

        if (profileId == Guid.Empty)
        {
            return;
        }

        _viewModel.Deactivate();

        var parameters = new Dictionary<string, object>
        {
            ["owner"] = _viewModel,
            ["barberProfileId"] = profileId,
        };

        if (item is not null)
        {
            parameters["item"] = item;
        }

        await Shell.Current.GoToAsync(BarberCabinetRoutes.ServiceEditor, parameters);
    }

    /// <summary>
    /// Открывает публичную карточку мастера по её идентификатору.
    /// </summary>
    /// <remarks>
    /// Кнопка видна только у подтверждённого и активного профиля: публичный маршрут
    /// показывает такую карточку клиентам, и превью неподтверждённой карточки ничего
    /// не проверяет — сервер отдал бы <c>404</c>.
    /// </remarks>
    private async void OnPreviewRequested(object? sender, EventArgs e)
    {
        var profileId = _viewModel.BarberProfileId;

        if (profileId == Guid.Empty)
        {
            return;
        }

        _viewModel.Deactivate();

        await Shell.Current.GoToAsync(
            string.Concat(BarberPage.Route, "?id=", profileId.ToString("D")));
    }

    /// <summary>
    /// Делится ссылкой на публичную карточку: текст и ссылка уходят в системный лист.
    /// </summary>
    private async void OnShareProfileRequested(object? sender, EventArgs e)
    {
        var profileId = _viewModel.BarberProfileId;

        if (profileId == Guid.Empty)
        {
            return;
        }

        var link = new Uri(
            _legalOptions.SiteBaseAddress,
            string.Concat("barbers/", profileId.ToString("D"))).ToString();

        try
        {
            await Share.Default.RequestAsync(
                new ShareTextRequest
                {
                    Title = Loc.T("Карточка мастера"),
                    Text = Loc.T("Моя карточка мастера: {0}", link),
                });
        }
        catch (Exception)
        {
            // Системный лист недоступен: профиль остаётся на экране, причина не блокирует работу.
        }
    }

    /// <summary>
    /// Открывает загрузку нового кадра портфолио.
    /// </summary>
    /// <param name="sender">Кнопка «Добавить фото».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnAddPhotoClicked(object? sender, EventArgs e) =>
        await OpenPhotoAsync(item: null);

    /// <summary>
    /// Открывает выбранный кадр портфолио: подпись, порядок, публикация и удаление.
    /// </summary>
    /// <param name="sender">Контейнер плитки.</param>
    /// <param name="e">Параметр нажатия — плитка фотографии.</param>
    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BarberProfilePhotoTile tile)
        {
            return;
        }

        var item = _viewModel.Photos.FirstOrDefault(photo => photo.Id == tile.PhotoId);

        if (item is null)
        {
            return;
        }

        await OpenPhotoAsync(item);
    }

    /// <summary>
    /// Переход на экран фото: раздел передаёт снимок списка, чтобы экран знал соседей.
    /// </summary>
    /// <remarks>
    /// Флаг <c>_isNavigating</c> гасит второе нажатие: два перехода подряд положили бы
    /// на стек две страницы одной фотографии. Раздел перед переходом засыпает — Shell
    /// не поднимает <c>OnDisappearing</c> у страницы, поверх которой открыли следующую.
    /// </remarks>
    /// <param name="item">Кадр для правки; <c>null</c> — загрузка нового.</param>
    /// <returns>Задача перехода.</returns>
    private async Task OpenPhotoAsync(BarberProfilePhotoItem? item)
    {
        var profileId = _viewModel.BarberProfileId;

        if (_isNavigating || profileId == Guid.Empty || Shell.Current is not { } shell)
        {
            return;
        }

        _isNavigating = true;

        try
        {
            _viewModel.Deactivate();

            var parameters = new Dictionary<string, object>
            {
                ["profileId"] = profileId,
                ["photos"] = _viewModel.Photos.ToList(),
            };

            if (item is not null)
            {
                parameters["photoId"] = item.Id;
            }

            await shell.GoToAsync(BarberPhotoPage.Route, parameters);
        }
        finally
        {
            _isNavigating = false;
        }
    }

    /// <summary>
    /// Открывает правку строки прайса через команду раздела.
    /// </summary>
    private void OnEditServiceClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberServiceItem item })
        {
            _viewModel.RequestEditServiceCommand.Execute(item);
        }
    }

    /// <summary>
    /// Скрывает услугу после подтверждения: строка остаётся в истории записей.
    /// </summary>
    private async void OnHideServiceClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: BarberServiceItem item })
        {
            return;
        }

        var page = DialogPage;

        if (page is null)
        {
            return;
        }

        var confirmed = await page.DisplayAlertAsync(
            Loc.T("Скрыть услугу?"),
            Loc.T("Услуга исчезнет из карточки и поиска. Записи, где она была, останутся в истории."),
            Loc.T("Скрыть"),
            Loc.T("Отмена"));

        if (confirmed)
        {
            await _viewModel.HideServiceCommand.ExecuteAsync(item);
        }
    }

    /// <summary>
    /// Удаляет аватар после подтверждения: файл исчезает безвозвратно, инициалы остаются.
    /// </summary>
    /// <remarks>
    /// Подтверждение спрашивает экран, а не ViewModel: диалог — часть показа, и команда
    /// удаления вызывается только после утвердительного ответа. Причина отказа ляжет
    /// в <c>AvatarError</c>, который раздел показывает отдельной строкой.
    /// </remarks>
    /// <param name="sender">Кнопка «Удалить».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnDeleteAvatarClicked(object? sender, EventArgs e)
    {
        var page = DialogPage;

        if (page is null)
        {
            return;
        }

        var confirmed = await page.DisplayAlertAsync(
            Loc.T("Удалить аватар?"),
            Loc.T("Аватар исчезнет из карточки и кабинета. Файл удаляется безвозвратно."),
            Loc.T("Удалить"),
            Loc.T("Отмена"));

        if (confirmed)
        {
            await _viewModel.DeleteAvatarCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Возвращает скрытую услугу в прайс с её ценой и длительностью.
    /// </summary>
    private async void OnRestoreServiceClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberServiceItem item })
        {
            await _viewModel.RestoreServiceCommand.ExecuteAsync(item);
        }
    }

    /// <summary>
    /// Убирает специализацию: удаление физическое, подтверждения в вебе нет.
    /// </summary>
    private async void OnRemoveSpecializationClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: BarberSpecializationItem item })
        {
            await _viewModel.RemoveSpecializationCommand.ExecuteAsync(item);
        }
    }

    /// <summary>
    /// Запоминает выбранную строку справочника: команда добавления принимает идентификатор.
    /// </summary>
    private void OnSpecializationSelected(object? sender, EventArgs e)
    {
        _selectedSpecializationId = (sender as Picker)?.SelectedItem is BarberSpecializationOption option
            ? option.Id
            : null;

        AddSpecializationButton.IsEnabled = _selectedSpecializationId is not null;
    }

    /// <summary>
    /// Закрывает пикер и сбрасывает локальный выбор: справочник остаётся прочитанным.
    /// </summary>
    /// <remarks>
    /// Команда закрывает пикер, но выбранная строка — состояние экрана, а не ViewModel:
    /// без сброса повторное открытие показало бы старый выбор и активную кнопку.
    /// </remarks>
    private void OnCloseSpecializationPickerClicked(object? sender, EventArgs e) =>
        ResetSpecializationSelection();

    /// <summary>
    /// Сбрасывает выбор специализации в пикере.
    /// </summary>
    private void ResetSpecializationSelection()
    {
        _selectedSpecializationId = null;
        AddSpecializationButton.IsEnabled = false;
        SpecializationPicker.SelectedIndex = -1;
    }

    /// <summary>
    /// Добавляет специализацию и сбрасывает пикер: справочник перечитывается вместе с карточкой.
    /// </summary>
    private async void OnAddSpecializationClicked(object? sender, EventArgs e)
    {
        if (_selectedSpecializationId is not { } specializationId)
        {
            return;
        }

        // Кнопка гаснет на время записи, выбор остаётся: после отказа справочник не
        // пересобирается, и человеку не нужно выбирать строку заново.
        AddSpecializationButton.IsEnabled = false;

        await _viewModel.AddSpecializationCommand.ExecuteAsync(specializationId);

        if (_viewModel.IsSpecializationPickerOpen)
        {
            AddSpecializationButton.IsEnabled = true;

            return;
        }

        ResetSpecializationSelection();
    }

    /// <summary>
    /// Раскладывает сетку по два кадра в строке: ширина плитки считается от ширины хоста.
    /// </summary>
    /// <remarks>
    /// Ширина известна только после раскладки, поэтому первая пара плиток приходит с нулевой
    /// шириной и получает настоящую здесь. Половина хоста без промежутка, а остаток от
    /// округления уходит в <c>SpaceBetween</c>: строка занята целиком, и две плитки не
    /// переносятся на вторую строку из-за дробной ширины. Поворот и планшет приходят тем же
    /// событием, так что сетка пересобирается без перечитывания карточки.
    /// </remarks>
    /// <param name="sender">Хост сетки.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnPhotoTilesHostSizeChanged(object? sender, EventArgs e)
    {
        var hostWidth = PhotoTilesHost.Width;

        if (hostWidth <= 0)
        {
            return;
        }

        var tileWidth = Math.Floor((hostWidth - PhotoTileGap) / 2);

        if (tileWidth < 1 || Math.Abs(tileWidth - _photoTileWidth) < 0.5)
        {
            return;
        }

        _photoTileWidth = tileWidth;

        foreach (var tile in _photoTiles)
        {
            tile.TileWidth = tileWidth;
        }
    }

    /// <summary>
    /// Фотографии пересобраны: строки ответа превращаются в плитки с абсолютными адресами.
    /// </summary>
    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildPhotoTiles();

    /// <summary>
    /// Строит плитки фотографий из карточки: адрес склеивается с базой API один раз.
    /// </summary>
    /// <remarks>
    /// Битый или пустой адрес пропускается: одна такая ссылка не должна ронять всю сетку.
    /// Публичный кадр загружает платформа по адресу, а неодобренный и скрытый так получить
    /// нельзя: раздача отвечает на анонимный запрос <c>404</c>, и байты читаются с Bearer —
    /// тем же путём, что и превью очередей модерации.
    /// </remarks>
    private void RebuildPhotoTiles()
    {
        _photoTiles.Clear();

        foreach (var photo in _viewModel.Photos)
        {
            var tileUrl = string.IsNullOrWhiteSpace(photo.ThumbnailUrl)
                ? MediaUrl.Resize(photo.Url, "md")
                : photo.ThumbnailUrl;

            if (string.IsNullOrWhiteSpace(tileUrl))
            {
                continue;
            }

            try
            {
                var imageUrl = MediaUrl.Resolve(_apiOptions.BaseAddress, tileUrl);
                var requiresBearer = !photo.IsVisibleToClients && IsApiMediaUrl(imageUrl);
                var tile = new BarberProfilePhotoTile(
                    photo.Id,
                    imageUrl,
                    photo.Caption,
                    photo.StatusText,
                    PhotoStatusKind(photo),
                    isVisibleToClients: !requiresBearer,
                    tileWidth: _photoTileWidth);

                _photoTiles.Add(tile);

                if (requiresBearer)
                {
                    _ = LoadPhotoAsync(tile, imageUrl);
                }
            }
            catch (ArgumentException)
            {
                // Некорректный адрес одной фотографии: остальные показываются как есть.
            }
        }
    }

    /// <summary>
    /// Ключ состояния кадра для бейджа проверки: тот же набор, что у статусов записей.
    /// </summary>
    /// <param name="photo">Кадр портфолио.</param>
    /// <returns><c>published</c>, <c>pending</c>, <c>rejected</c> либо <c>hidden</c>.</returns>
    private static string PhotoStatusKind(BarberProfilePhotoItem photo) =>
        photo.IsBlocked || photo.ModerationStatus == PhotoModerationStatus.Rejected
            ? "rejected"
            : photo.ModerationStatus == PhotoModerationStatus.Pending
                ? "pending"
                : photo.IsPublished
                    ? "published"
                    : "hidden";

    /// <summary>
    /// Читает неодобренный кадр с Bearer и показывает его, не ожидая остальных плиток.
    /// </summary>
    /// <remarks>
    /// Одна попытка без повтора, как у превью модерации: кадр — украшение строки, а не
    /// данные. Сбой оставляет плитку подложкой, состояние проверки видно подписью.
    /// </remarks>
    /// <param name="tile">Плитка, которой нужна картинка.</param>
    /// <param name="imageUrl">Абсолютный адрес миниатюры.</param>
    /// <returns>Задача чтения.</returns>
    private async Task LoadPhotoAsync(BarberProfilePhotoTile tile, string imageUrl)
    {
        try
        {
            var bytes = await _mediaApi.GetBytesAsync(imageUrl, CancellationToken.None);

            tile.ApplyImageBytes(bytes);
        }
        catch (BarberSearchApiException)
        {
            // Кадр не пришёл: плитка остаётся подложкой, причина видна по статусу проверки.
        }
        catch (HttpRequestException)
        {
            // Сеть недоступна: остальные плитки грузятся независимо.
        }
        catch (OperationCanceledException)
        {
            // Отмена транспорта: показывать нечего, как и у очередей модерации.
        }
    }

    /// <summary>
    /// Адрес, состояние проверки или блокировка аватара изменились: картинку пора перечитать.
    /// </summary>
    /// <param name="sender">ViewModel раздела.</param>
    /// <param name="e">Имя изменившегося свойства.</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BarberCabinetProfileViewModel.AvatarUrl)
            or nameof(BarberCabinetProfileViewModel.AvatarStatusKind)
            or nameof(BarberCabinetProfileViewModel.IsAvatarBlocked))
        {
            _ = RefreshAvatarImageAsync();
        }
    }

    /// <summary>
    /// Показывает аватар: свой неодобренный кадр читается с Bearer, остальные — по адресу.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Инициалы лежат под картинкой и остаются запасным вариантом: пока кадр не пришёл,
    /// отклонён или скрыт администратором, круг не пустеет. Раздача отвечает на анонимный
    /// запрос <c>404</c> для неодобренного файла, поэтому «на проверке» и «отклонено»
    /// читаются тем же путём, что превью очередей модерации.
    /// </para>
    /// <para>
    /// Счётчик поколений отбрасывает устаревшие ответы: смена адреса или статуса запускает
    /// новую загрузку, и прежняя не должна перезаписать свежую картинку. Ошибки глушатся —
    /// аватар украшение карточки, а не данные; причина видна по бейджу состояния.
    /// </para>
    /// </remarks>
    /// <returns>Задача чтения.</returns>
    private async Task RefreshAvatarImageAsync()
    {
        var generation = ++_avatarGeneration;
        var rawUrl = _viewModel.AvatarUrl;

        if (string.IsNullOrWhiteSpace(rawUrl) || _viewModel.IsAvatarBlocked)
        {
            ApplyAvatarImage(source: null);

            return;
        }

        string imageUrl;

        try
        {
            imageUrl = MediaUrl.Resolve(_apiOptions.BaseAddress, MediaUrl.Resize(rawUrl, "md"));
        }
        catch (ArgumentException)
        {
            ApplyAvatarImage(source: null);

            return;
        }

        ImageSource? source = null;

        try
        {
            if (IsApiMediaUrl(imageUrl) && _viewModel.AvatarStatusKind is "pending" or "rejected")
            {
                var bytes = await _mediaApi.GetBytesAsync(imageUrl, CancellationToken.None);

                if (generation != _avatarGeneration)
                {
                    return;
                }

                if (bytes.Length > 0)
                {
                    source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
                }
            }
            else
            {
                // Одобренный свой файл и внешняя ссылка доступны анонимно: платформа
                // загрузит их по адресу и закеширует.
                source = ImageSource.FromUri(new Uri(imageUrl));
            }
        }
        catch (BarberSearchApiException)
        {
            // Кадр не пришёл: остаются инициалы, состояние проверки видно бейджем.
        }
        catch (HttpRequestException)
        {
            // Сеть недоступна: заглушка с инициалами остаётся на месте.
        }
        catch (OperationCanceledException)
        {
            // Отмена транспорта: показывать нечего, как и у очередей модерации.
        }
        catch (ArgumentException)
        {
            // Адрес не сложился в URI: картинка не показывается, инициалы остаются.
        }

        if (generation != _avatarGeneration)
        {
            return;
        }

        ApplyAvatarImage(source);
    }

    /// <summary>
    /// Применяет картинку аватара: <c>null</c> возвращает заглушку с инициалами.
    /// </summary>
    /// <param name="source">Готовая картинка либо <c>null</c>, если показывать нечего.</param>
    private void ApplyAvatarImage(ImageSource? source)
    {
        AvatarImage.Source = source;
        AvatarImage.IsVisible = source is not null;
    }

    /// <summary>
    /// Адрес принадлежит нашей раздаче медиа: только ей нужен Bearer и можно слать токен.
    /// </summary>
    /// <param name="imageUrl">Абсолютный адрес картинки.</param>
    /// <returns><c>true</c>, если это файл <c>api/media</c> того же API.</returns>
    private bool IsApiMediaUrl(string imageUrl) =>
        Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
        && uri.AbsolutePath.StartsWith("/api/media/", StringComparison.Ordinal)
        && string.Equals(
            uri.GetLeftPart(UriPartial.Authority),
            _apiOptions.BaseAddress.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Страница, на которой показывается диалог: активная страница Shell.
    /// </summary>
    private static Page? DialogPage =>
        Shell.Current?.CurrentPage ?? (Page?)Shell.Current;
}

/// <summary>
/// Плитка фотографии портфолио в кабинете: идентификатор, адрес картинки и подписи.
/// </summary>
/// <remarks>
/// <para>
/// Проекция нужна только разметке: <see cref="BarberProfilePhotoItem"/> отдаёт
/// относительный адрес, а <c>Image</c> ждёт абсолютный. Тип лежит в приложении, потому
/// что склейку с базой API делает экран, а не Core.
/// </para>
/// <para>
/// Идентификатор кадра переезжает в плитку: по нажатию раздел находит исходную строку
/// в <c>Photos</c> и передаёт её экрану фото — второй копии данных в плитке нет.
/// </para>
/// <para>
/// Публичный кадр грузит платформа по адресу; неодобренный так получить нельзя — раздача
/// отвечает на анонимный запрос <c>404</c>, — поэтому его байты приносит
/// <see cref="IAdminMediaApi"/>, а картинка собирается из потока в памяти. Свойство
/// наблюдаемое: байты приходят после того, как плитка уже на экране.
/// </para>
/// </remarks>
public sealed class BarberProfilePhotoTile : INotifyPropertyChanged
{
    /// <summary>
    /// Отступ карточки кадра: p-2 карточки веба, 8.
    /// </summary>
    private const double Padding = 8;

    private ImageSource? _image;
    private double _tileWidth;

    /// <summary>
    /// Создаёт плитку из строки ответа.
    /// </summary>
    /// <param name="photoId">Идентификатор фотографии.</param>
    /// <param name="imageUrl">Абсолютный адрес миниатюры.</param>
    /// <param name="caption">Подпись владельца либо «Без подписи».</param>
    /// <param name="statusText">Состояние проверки кадра.</param>
    /// <param name="statusKind">Ключ состояния проверки: цвет бейджа.</param>
    /// <param name="isVisibleToClients">
    /// Кадр доступен анонимно и его можно отдать загрузчику картинок платформы.
    /// </param>
    /// <param name="tileWidth">Ширина плитки; <c>0</c>, пока сетка не разложена.</param>
    public BarberProfilePhotoTile(
        Guid photoId,
        string imageUrl,
        string caption,
        string statusText,
        string statusKind,
        bool isVisibleToClients,
        double tileWidth = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentNullException.ThrowIfNull(statusText);
        ArgumentNullException.ThrowIfNull(statusKind);

        PhotoId = photoId;
        ImageUrl = imageUrl;
        Image = isVisibleToClients ? ImageSource.FromUri(new Uri(imageUrl)) : null;
        Caption = caption;
        StatusText = statusText;
        StatusKind = statusKind;
        _tileWidth = tileWidth;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Идентификатор фотографии: по нему нажатие находит строку в разделе.
    /// </summary>
    public Guid PhotoId { get; }

    /// <summary>
    /// Абсолютный адрес миниатюры.
    /// </summary>
    public string ImageUrl { get; }

    /// <summary>
    /// Картинка для <c>Image.Source</c>; <c>null</c>, пока неодобренный кадр не прочитан.
    /// </summary>
    public ImageSource? Image
    {
        get => _image;
        private set
        {
            _image = value;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
        }
    }

    /// <summary>
    /// Ширина плитки: половина строки сетки без промежутка, считается разделом.
    /// </summary>
    /// <remarks>
    /// Ноль — «ширина ещё не известна»: сетка раскладывается после первого замера, и
    /// <see cref="ImageSize"/> тогда тоже ноль. Сетка сообщает ширину всем плиткам сразу,
    /// поэтому поворот и планшет перекладывают её без перечитывания карточки.
    /// </remarks>
    public double TileWidth
    {
        get => _tileWidth;
        set
        {
            if (Math.Abs(_tileWidth - value) < 0.5)
            {
                return;
            }

            _tileWidth = value;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSize)));
        }
    }

    /// <summary>
    /// Сторона квадратного превью: ширина плитки без отступов карточки.
    /// </summary>
    public double ImageSize => Math.Max(0, TileWidth - (2 * Padding));

    /// <summary>
    /// Подпись фотографии либо «Без подписи».
    /// </summary>
    public string Caption { get; }

    /// <summary>
    /// Состояние проверки: «Опубликовано», «На проверке», «Отклонено».
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Ключ состояния проверки: <c>published</c>, <c>pending</c>, <c>rejected</c> или
    /// <c>hidden</c>. Им бейдж выбирает цвет — тот же набор, что у статусов записей.
    /// </summary>
    public string StatusKind { get; }

    /// <summary>
    /// Показывает кадр, байты которого прочитаны с Bearer.
    /// </summary>
    /// <param name="bytes">Содержимое файла.</param>
    internal void ApplyImageBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
        {
            return;
        }

        Image = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
    }
}
