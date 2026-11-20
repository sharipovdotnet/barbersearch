using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран одного фото портфолио: загрузка нового кадра или правка уже загруженного.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута и живёт один визит. «Профиль» кабинета
/// передаёт ей идентификатор профиля и снимок списка фотографий, а для правки — ещё и
/// идентификатор кадра: без него экран открывает загрузку, с ним — действия над кадром.
/// Список передаётся снимком, поэтому после любой успешной мутации экран закрывается,
/// а раздел перечитывает карточку на возврате.
/// </para>
/// <para>
/// Подписка на <see cref="BarberPhotoViewModel.Completed"/> не снимается: и страница, и
/// ViewModel создаются на один визит, и контейнер держит только страницу. После возврата
/// пара «страница — ViewModel» уходит в сборку мусора вместе с подпиской, а закрытая
/// страница уже не на виду, чтобы среагировать на событие.
/// </para>
/// <para>
/// Превью одобренного кадра грузит платформа по адресу; неодобренный, скрытый и
/// отклонённый так получить нельзя — раздача media отвечает анонимному запросу «404», —
/// поэтому его байты читает приложение с Bearer, как и плитки в разделе.
/// </para>
/// </remarks>
public partial class BarberPhotoPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "barber-photo";

    private readonly BarberPhotoViewModel _viewModel;
    private readonly BarberSearchApiOptions _apiOptions;
    private readonly IAdminMediaApi _mediaApi;

    private bool _queryApplied;
    private bool _isNavigatingBack;
    private string? _previewUrl;

    /// <summary>
    /// Создаёт экран фотографии.
    /// </summary>
    /// <param name="viewModel">Состояние экрана: режим, поля и команды.</param>
    /// <param name="apiOptions">Базовый адрес API: относительные адреса фото склеиваются им.</param>
    /// <param name="mediaApi">Чтение неодобренных кадров с Bearer: анониму раздача отвечает «404».</param>
    public BarberPhotoPage(
        BarberPhotoViewModel viewModel,
        BarberSearchApiOptions apiOptions,
        IAdminMediaApi mediaApi)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(apiOptions);
        ArgumentNullException.ThrowIfNull(mediaApi);

        InitializeComponent();

        _viewModel = viewModel;
        _apiOptions = apiOptions;
        _mediaApi = mediaApi;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает профиль, снимок списка фотографий и необязательный идентификатор кадра.
    /// </summary>
    /// <remarks>
    /// Без профиля и списка работать не с чем: Shell создаёт страницу раньше, чем становится
    /// известно содержимое, поэтому единственный разумный выход — сразу закрыться. Кадр,
    /// которого уже нет в списке, экран не считает ошибкой перехода: ViewModel показывает
    /// «Фото не найдено» и оставляет действия выключенными.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("profileId", out var profileValue)
            || profileValue is not Guid profileId
            || profileId == Guid.Empty
            || !query.TryGetValue("photos", out var photosValue)
            || photosValue is not IReadOnlyList<BarberProfilePhotoItem> photos)
        {
            CloseImmediately();

            return;
        }

        var photoId = query.TryGetValue("photoId", out var photoValue) && photoValue is Guid target
            ? target
            : (Guid?)null;

        _queryApplied = true;

        _viewModel.Completed += OnCompleted;

        if (photoId is { } photoToEdit)
        {
            _viewModel.InitializeEdit(profileId, photos, photoToEdit);
            ApplyPhotoPreview();
        }
        else
        {
            _viewModel.InitializeNew(profileId, photos.Count);
        }
    }

    /// <summary>
    /// Спрашивает подтверждение и удаляет кадр: удаление необратимо.
    /// </summary>
    /// <param name="sender">Кнопка «Удалить».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            Loc.T("Удалить фото?"),
            Loc.T("Фото исчезнет из карточки и поиска. Это необратимо."),
            Loc.T("Удалить"),
            Loc.T("Отмена"));

        if (confirmed)
        {
            await _viewModel.DeleteCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Закрывает экран после успешной мутации: раздел перечитает карточку на возврате.
    /// </summary>
    /// <param name="sender">ViewModel экрана.</param>
    /// <param name="e">Событие без данных.</param>
    private async void OnCompleted(object? sender, EventArgs e)
    {
        if (_isNavigatingBack)
        {
            return;
        }

        _isNavigatingBack = true;

        try
        {
            if (Shell.Current is { } shell)
            {
                await shell.GoToAsync("..");
            }
        }
        finally
        {
            _isNavigatingBack = false;
        }
    }

    /// <summary>
    /// Показывает превью загруженного кадра: адрес склеивается с базой API один раз.
    /// </summary>
    /// <remarks>
    /// Некорректный адрес оставляет подложку: одна битая ссылка не должна ронять экран.
    /// Внешнюю ссылку грузит платформа, а нашу непубличную раздачу — приложение с Bearer.
    /// </remarks>
    private void ApplyPhotoPreview()
    {
        if (_viewModel.Photo is not { } photo)
        {
            _previewUrl = null;
            PhotoPreviewImage.Source = null;

            return;
        }

        if (!string.IsNullOrWhiteSpace(photo.RejectionReason))
        {
            RejectionReasonLabel.Text = Loc.T("Причина: {0}", photo.RejectionReason);
        }

        string imageUrl;

        try
        {
            imageUrl = MediaUrl.Resolve(
                _apiOptions.BaseAddress,
                MediaUrl.Resize(photo.Url, "lg"));
        }
        catch (ArgumentException)
        {
            return;
        }

        _previewUrl = imageUrl;

        try
        {
            if (photo.IsVisibleToClients || !IsApiMediaUrl(imageUrl))
            {
                PhotoPreviewImage.Source = ImageSource.FromUri(new Uri(imageUrl));

                return;
            }
        }
        catch (UriFormatException)
        {
            return;
        }

        _ = LoadPhotoPreviewAsync(imageUrl);
    }

    /// <summary>
    /// Читает непубличный кадр с Bearer и показывает его, не дожидаясь ответа страницы.
    /// </summary>
    /// <remarks>
    /// Одна попытка без повтора, как у превью модерации: кадр — украшение экрана, а не
    /// данные. Сбой оставляет подложку, состояние проверки видно подписью.
    /// </remarks>
    /// <param name="imageUrl">Абсолютный адрес кадра.</param>
    /// <returns>Задача чтения.</returns>
    private async Task LoadPhotoPreviewAsync(string imageUrl)
    {
        try
        {
            var bytes = await _mediaApi.GetBytesAsync(imageUrl, CancellationToken.None);

            if (bytes.Length == 0
                || !string.Equals(_previewUrl, imageUrl, StringComparison.Ordinal))
            {
                return;
            }

            PhotoPreviewImage.Source = ImageSource.FromStream(
                () => new MemoryStream(bytes, writable: false));
        }
        catch (BarberSearchApiException)
        {
            // Кадр не пришёл: остаётся подложка, причина видна по статусу проверки.
        }
        catch (HttpRequestException)
        {
            // Сеть недоступна: экран остаётся рабочим, действия не блокируются.
        }
        catch (OperationCanceledException)
        {
            // Отмена транспорта: показывать нечего.
        }
    }

    /// <summary>
    /// Адрес принадлежит нашей раздаче медиа: только ей можно слать Bearer.
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
    /// Закрывает экран, открытый без обязательных параметров.
    /// </summary>
    /// <remarks>
    /// Закрытие отложено до диспетчера: Shell ещё не завершил навигацию, когда страница
    /// получает параметры.
    /// </remarks>
    private void CloseImmediately()
    {
        Dispatcher.Dispatch(async () =>
        {
            if (Shell.Current is { } shell)
            {
                await shell.GoToAsync("..");
            }
        });
    }
}
