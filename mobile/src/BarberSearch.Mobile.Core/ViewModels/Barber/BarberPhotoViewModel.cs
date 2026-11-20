using System.Globalization;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Страница одной фотографии портфолио: загрузка новой или правка существующей.
/// </summary>
/// <remarks>
/// <para>
/// Один ViewModel на два режима, потому что оба экрана делят подпись, состояния и ошибки.
/// <see cref="InitializeNew"/> готовит добавление: файла ещё нет, позиция — следующая за
/// последней. <see cref="InitializeEdit"/> находит кадр по идентификатору в уже прочитанном
/// списке и включает действия строки: порядок, подпись, публикацию и удаление.
/// </para>
/// <para>
/// Правила взяты у веба (<c>frontend/src/components/barber/barber-profile-photos.tsx</c>):
/// заблокированный администратором кадр можно только удалить, у неодобренного нет
/// «Скрыть»/«Вернуть» — публичность всё равно решает модерация, а порядок меняется целым
/// списком идентификаторов в одном <c>PUT</c>, чтобы частичный сбой не дал дубли
/// <c>SortOrder</c>.
/// </para>
/// <para>
/// Экран не перечитывает карточку сам: успешное действие поднимает <see cref="Completed"/>,
/// и страница обновляет список. Поэтому данные при отказе не меняются — человек видит ту же
/// фотографию и причину, а не пустой экран.
/// </para>
/// </remarks>
public sealed partial class BarberPhotoViewModel : ObservableObject
{
    private readonly IBarberProfileApi _api;
    private readonly IPortfolioPhotoPicker _picker;

    private Guid _barberProfileId;
    private Guid[] _photoIds = [];
    private BarberProfilePhotoItem? _photo;
    private byte[]? _pickedPhotoBytes;
    private string _pickedFileName = string.Empty;
    private string _caption = string.Empty;
    private int _existingPhotoCount;
    private int _position;
    private int _photoCount;
    private int _index = -1;
    private bool _isNew;
    private bool _isBusy;
    private bool _isUploading;
    private string? _errorText;
    private string? _fileErrorText;
    private string? _captionErrorText;

    /// <summary>
    /// Создаёт страницу фотографии.
    /// </summary>
    /// <param name="api">Загрузка, удаление, подпись, порядок и публикация.</param>
    /// <param name="picker">Системный выбор файла с подготовкой кадра в приложении.</param>
    public BarberPhotoViewModel(IBarberProfileApi api, IPortfolioPhotoPicker picker)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(picker);

        _api = api;
        _picker = picker;
    }

    /// <summary>
    /// Режим добавления: фотографии ещё нет, файл выбирается.
    /// </summary>
    public bool IsNew => _isNew;

    /// <summary>
    /// Профиль мастера, к портфолио которого относится страница.
    /// </summary>
    public Guid BarberProfileId => _barberProfileId;

    /// <summary>
    /// Редактируемая фотография; <c>null</c> в режиме добавления и когда кадр не найден.
    /// </summary>
    public BarberProfilePhotoItem? Photo => _photo;

    /// <summary>
    /// Заголовок страницы: «Новое фото» или «Фото».
    /// </summary>
    /// <remarks>
    /// По-русски заголовок остаётся исходной строкой «Фото». Для английского каталога
    /// взят отдельный ключ «Фотография»: ключ «Фото» уже занят разделом админки и
    /// переводится как «Photos», а заголовок одного кадра должен читаться в единственном
    /// числе. Оба варианта проходят через <see cref="Loc.T"/>, поэтому выбор языка не
    /// добавляет второй механизм перевода.
    /// </remarks>
    public string TitleText => _isNew
        ? Loc.T("Новое фото")
        : Loc.Locale == Locale.En
            ? Loc.T("Фотография")
            : Loc.T("Фото");

    /// <summary>
    /// Позиция кадра: «Фото 2 из 5».
    /// </summary>
    public string PositionText => _position > 0 && _photoCount > 0
        ? Loc.T(
            "Фото {0} из {1}",
            _position.ToString(CultureInfo.InvariantCulture),
            _photoCount.ToString(CultureInfo.InvariantCulture))
        : string.Empty;

    /// <summary>
    /// Подпись, которую правит человек; пустая строка стирает подпись при сохранении.
    /// </summary>
    public string Caption
    {
        get => _caption;
        set
        {
            if (SetProperty(ref _caption, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CaptionLength));
                OnPropertyChanged(nameof(CaptionCounterText));
            }
        }
    }

    /// <summary>
    /// Длина подписи в символах — по ней считается счётчик.
    /// </summary>
    public int CaptionLength => Caption.Length;

    /// <summary>
    /// Счётчик подписи: «12/2000».
    /// </summary>
    public string CaptionCounterText => string.Concat(
        Caption.Length.ToString(CultureInfo.InvariantCulture),
        "/",
        PortfolioPhotoLimits.MaxDescriptionLength.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Состояние проверки кадра: «Опубликовано», «На проверке», «Отклонено» и соседние.
    /// </summary>
    public string StatusText => _photo?.StatusText ?? string.Empty;

    /// <summary>
    /// Причина отказа администратора; <c>null</c>, если отказа нет.
    /// </summary>
    public string? RejectionReason => _photo?.RejectionReason;

    /// <summary>
    /// Причина отказа заполнена и показывается под статусом.
    /// </summary>
    public bool ShowRejectionReason => !string.IsNullOrWhiteSpace(RejectionReason);

    /// <summary>
    /// Кадр можно поднять выше.
    /// </summary>
    public bool CanMoveUp =>
        !_isNew && _photo is not null && !IsBlocked && _index > 0;

    /// <summary>
    /// Кадр можно опустить ниже.
    /// </summary>
    public bool CanMoveDown =>
        !_isNew && _photo is not null && !IsBlocked && _index >= 0 && _index < _photoCount - 1;

    /// <summary>
    /// Подпись можно править: кадр есть и не заблокирован администратором.
    /// </summary>
    public bool CanEditCaption => !_isNew && _photo is not null && !IsBlocked;

    /// <summary>
    /// Публикацию можно переключить: кадр не заблокирован и одобрен модератором.
    /// </summary>
    public bool CanTogglePublication =>
        !_isNew && _photo is not null && !IsBlocked && _photo.IsApproved;

    /// <summary>
    /// Кадр можно удалить; удаление необратимо.
    /// </summary>
    public bool CanDelete => !_isNew && _photo is not null;

    /// <summary>
    /// Подпись действия публикации: «Скрыть» для опубликованного кадра, «Вернуть» — для скрытого.
    /// </summary>
    public string PublicationActionText => _photo?.IsPublished == true
        ? Loc.T("Скрыть")
        : Loc.T("Вернуть");

    /// <summary>
    /// Файл выбран и готов к загрузке.
    /// </summary>
    public bool HasPickedPhoto => _pickedPhotoBytes is { Length: > 0 };

    /// <summary>
    /// Байты выбранного файла; <c>null</c>, пока файл не выбран.
    /// </summary>
    public byte[]? PickedPhotoBytes => _pickedPhotoBytes;

    /// <summary>
    /// Имя выбранного файла для предпросмотра и multipart-части.
    /// </summary>
    public string PickedFileName => _pickedFileName;

    /// <summary>
    /// Идёт запрос: команды недоступны, чтобы не отправить две мутации.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSubmitUpload));
                NotifyCommands();
            }
        }
    }

    /// <summary>
    /// Идёт загрузка файла: кнопка показывает индикатор, остальные действия уже заблокированы.
    /// </summary>
    public bool IsUploading
    {
        get => _isUploading;
        private set => SetProperty(ref _isUploading, value);
    }

    /// <summary>
    /// Загрузку можно отправить: режим добавления, есть файл, лимит не исчерпан и нет запроса.
    /// </summary>
    public bool CanSubmitUpload =>
        _isNew
        && _existingPhotoCount < PortfolioPhotoLimits.MaxPhotos
        && HasPickedPhoto
        && !IsBusy;

    /// <summary>
    /// Общая причина отказа над списком; <c>null</c>, когда ошибки нет.
    /// </summary>
    public string? ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    /// <summary>
    /// Причина отказа у поля «Файл».
    /// </summary>
    public string? FileErrorText
    {
        get => _fileErrorText;
        private set => SetProperty(ref _fileErrorText, value);
    }

    /// <summary>
    /// Причина отказа у поля «Подпись».
    /// </summary>
    public string? CaptionErrorText
    {
        get => _captionErrorText;
        private set => SetProperty(ref _captionErrorText, value);
    }

    /// <summary>
    /// Действие выполнено: страница закрывает форму и перечитывает портфолио.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Готовит добавление фотографии.
    /// </summary>
    /// <remarks>
    /// Позиция считается по уже загруженным кадрам: «Фото 4 из 4» при трёх в портфолио.
    /// Лимит проверяется по числу существующих фотографий, а не по позиции: десятая
    /// фотография разрешена, одиннадцатая — нет.
    /// </remarks>
    /// <param name="barberProfileId">Профиль мастера.</param>
    /// <param name="photoCount">Сколько фотографий уже в портфолио.</param>
    public void InitializeNew(Guid barberProfileId, int photoCount)
    {
        EnsureProfileId(barberProfileId);

        if (photoCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(photoCount),
                "Число фотографий не может быть отрицательным.");
        }

        Reset(barberProfileId);

        _isNew = true;
        _existingPhotoCount = photoCount;
        _photoCount = photoCount + 1;
        _position = photoCount + 1;
        _index = -1;
        _photoIds = [];

        NotifyAll();
    }

    /// <summary>
    /// Готовит правку существующей фотографии.
    /// </summary>
    /// <remarks>
    /// Список копируется защитно: страница может перечитать коллекцию после возврата, а
    /// команда порядка обязана отправить тот набор, который был на экране, — иначе
    /// перестановка соседей подменила бы чужой кадр. Если фотографии уже нет в списке,
    /// страница показывает «Фото не найдено» и оставляет действия выключенными.
    /// </remarks>
    /// <param name="barberProfileId">Профиль мастера.</param>
    /// <param name="photos">Все фотографии портфолио в порядке сервера.</param>
    /// <param name="photoId">Редактируемая фотография.</param>
    public void InitializeEdit(
        Guid barberProfileId,
        IReadOnlyList<BarberProfilePhotoItem> photos,
        Guid photoId)
    {
        ArgumentNullException.ThrowIfNull(photos);
        EnsureProfileId(barberProfileId);

        Reset(barberProfileId);

        _isNew = false;
        _photoCount = photos.Count;
        _photoIds = new Guid[photos.Count];

        for (var i = 0; i < photos.Count; i++)
        {
            _photoIds[i] = photos[i].Id;
        }

        var index = Array.FindIndex(_photoIds, id => id == photoId);

        if (index < 0)
        {
            _index = -1;
            _position = 0;
            _photo = null;
            ErrorText = Loc.T("Фото не найдено. Обновите страницу.");
        }
        else
        {
            _index = index;
            _position = index + 1;
            _photo = photos[index];
            _caption = _photo.Description ?? string.Empty;
        }

        NotifyAll();
    }

    /// <summary>
    /// Выбирает файл через системный пикер и готовит его к загрузке.
    /// </summary>
    /// <remarks>
    /// Отмена выбора — не ошибка: <c>null</c> от пикера оставляет прежнее состояние и не
    /// показывает текст. Ошибка подготовки приходит кодом и переводится в строку у поля
    /// «Файл»; прочие исключения считаются «не удалось прочитать изображение».
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanPickPhoto))]
    private async Task PickPhotoAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        FileErrorText = null;
        ErrorText = null;
        IsBusy = true;

        try
        {
            var image = await _picker.PickAsync(cancellationToken);

            if (image is null)
            {
                return;
            }

            _pickedPhotoBytes = image.Bytes;
            _pickedFileName = image.FileName;
            FileErrorText = null;

            OnPropertyChanged(nameof(HasPickedPhoto));
            OnPropertyChanged(nameof(PickedPhotoBytes));
            OnPropertyChanged(nameof(PickedFileName));
            OnPropertyChanged(nameof(CanSubmitUpload));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Человек ушёл со страницы: пикер закрыт, причина не показывается.
        }
        catch (PortfolioImageException exception)
        {
            FileErrorText = PortfolioPhotoErrors.DescribePicker(exception.Kind).Message;
        }
        catch (Exception)
        {
            FileErrorText = Loc.T("Не удалось прочитать изображение. Выберите другой файл.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Загружает выбранный файл с подписью.
    /// </summary>
    /// <remarks>
    /// Подпись проверяется до запроса: сервер отверг бы слишком длинную тем же кодом, но
    /// человеку незачем ждать сеть ради правила, известного клиенту. Отправляется JPEG:
    /// пикер уже перекодировал кадр, поэтому MIME-тип фиксирован.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync(CancellationToken cancellationToken)
    {
        if (!CanSubmitUpload)
        {
            return;
        }

        ClearErrors();

        var trimmed = Caption.Trim();

        if (trimmed.Length > PortfolioPhotoLimits.MaxDescriptionLength)
        {
            CaptionErrorText = Loc.T("Подпись не длиннее 2000 символов.");

            return;
        }

        IsBusy = true;
        IsUploading = true;

        try
        {
            var description = trimmed.Length == 0 ? null : trimmed;

            await _api.UploadPhotoAsync(
                _barberProfileId,
                new PortfolioPhotoUpload(
                    _pickedPhotoBytes!,
                    _pickedFileName,
                    "image/jpeg",
                    description),
                cancellationToken);

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена вызывающим: результат неизвестен, но ошибку не показываем.
        }
        catch (Exception exception)
        {
            ApplyFailure(exception);
        }
        finally
        {
            IsBusy = false;
            IsUploading = false;
        }
    }

    /// <summary>
    /// Сохраняет подпись существующей фотографии.
    /// </summary>
    /// <remarks>
    /// Пустое поле стирает подпись: сервер хранит отсутствие текста как <c>null</c>, а не
    /// как пустую строку. Проверка длины — та же, что у загрузки.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanSaveCaption))]
    private async Task SaveCaptionAsync(CancellationToken cancellationToken)
    {
        if (_isNew || _photo is not { } photo || !CanEditCaption || IsBusy)
        {
            return;
        }

        ClearErrors();

        var trimmed = Caption.Trim();

        if (trimmed.Length > PortfolioPhotoLimits.MaxDescriptionLength)
        {
            CaptionErrorText = Loc.T("Подпись не длиннее 2000 символов.");

            return;
        }

        IsBusy = true;

        try
        {
            await _api.UpdatePhotoDescriptionAsync(
                _barberProfileId,
                photo.Id,
                trimmed.Length == 0 ? null : trimmed,
                cancellationToken);

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена вызывающим: подпись на сервере не изменилась, текст не нужен.
        }
        catch (Exception exception)
        {
            ApplyFailure(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Поднимает фотографию на одну позицию выше.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanMoveUpPhoto))]
    private Task MoveUpAsync(CancellationToken cancellationToken) =>
        MoveAsync(-1, cancellationToken);

    /// <summary>
    /// Опускает фотографию на одну позицию ниже.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanMoveDownPhoto))]
    private Task MoveDownAsync(CancellationToken cancellationToken) =>
        MoveAsync(1, cancellationToken);

    /// <summary>
    /// Переключает публикацию: «Скрыть» для опубликованного кадра, «Вернуть» — для скрытого.
    /// </summary>
    /// <remarks>
    /// У неодобренного кадра действия нет: публичность решает модерация, и кнопка
    /// обещала бы результат, которого сервер не даст.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanTogglePublicationPhoto))]
    private async Task TogglePublicationAsync(CancellationToken cancellationToken)
    {
        if (_isNew || _photo is not { } photo || !CanTogglePublication || IsBusy)
        {
            return;
        }

        ClearErrors();
        IsBusy = true;

        try
        {
            if (photo.IsPublished)
            {
                await _api.UnpublishPhotoAsync(
                    _barberProfileId,
                    photo.Id,
                    cancellationToken);
            }
            else
            {
                await _api.PublishPhotoAsync(
                    _barberProfileId,
                    photo.Id,
                    cancellationToken);
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена вызывающим: публичность на сервере не изменилась.
        }
        catch (Exception exception)
        {
            ErrorText = PortfolioPhotoErrors.Describe(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Удаляет фотографию; подтверждение спрашивает страница.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanDeletePhoto))]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (_isNew || _photo is not { } photo || IsBusy)
        {
            return;
        }

        ClearErrors();
        IsBusy = true;

        try
        {
            await _api.RemovePhotoAsync(
                _barberProfileId,
                photo.Id,
                cancellationToken);

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена вызывающим: кадр на сервере остался, ошибку не показываем.
        }
        catch (Exception exception)
        {
            ErrorText = PortfolioPhotoErrors.Describe(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Меняет местами текущий кадр и соседа одним запросом с полным списком.
    /// </summary>
    /// <param name="direction">Направление: <c>-1</c> — выше, <c>1</c> — ниже.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task MoveAsync(int direction, CancellationToken cancellationToken)
    {
        if (_isNew || _photo is null || IsBusy)
        {
            return;
        }

        // Команда проверяет CanExecute, но прямой вызов всё равно обязан уважать край
        // списка и блокировку администратора: иначе повторная команда переставила бы кадр,
        // который владельцу недоступен.
        if ((direction < 0 && !CanMoveUp) || (direction > 0 && !CanMoveDown))
        {
            return;
        }

        var target = _index + direction;

        if (_index < 0 || target < 0 || target >= _photoIds.Length)
        {
            return;
        }

        ClearErrors();
        IsBusy = true;

        try
        {
            var ids = (Guid[])_photoIds.Clone();
            (ids[_index], ids[target]) = (ids[target], ids[_index]);

            await _api.ReorderPhotosAsync(_barberProfileId, ids, cancellationToken);

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена вызывающим: порядок на сервере остался прежним.
        }
        catch (Exception exception)
        {
            ErrorText = PortfolioPhotoErrors.Describe(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Начинает состояние заново для другого профиля.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера.</param>
    private void Reset(Guid barberProfileId)
    {
        _barberProfileId = barberProfileId;
        _photoIds = [];
        _photo = null;
        _pickedPhotoBytes = null;
        _pickedFileName = string.Empty;
        _caption = string.Empty;
        _existingPhotoCount = 0;
        _position = 0;
        _photoCount = 0;
        _index = -1;
        _isNew = false;
        _isBusy = false;
        _isUploading = false;
        _errorText = null;
        _fileErrorText = null;
        _captionErrorText = null;

        NotifyAll();
    }

    /// <summary>
    /// Отсекает пустой идентификатор профиля: запрос всё равно некуда адресовать.
    /// </summary>
    private static void EnsureProfileId(Guid barberProfileId)
    {
        if (barberProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор профиля не может быть пустым.",
                nameof(barberProfileId));
        }
    }

    /// <summary>
    /// Раскладывает отказ по полю: файл, подпись или общая строка.
    /// </summary>
    private void ApplyFailure(Exception exception)
    {
        var failure = PortfolioPhotoErrors.Describe(exception);

        switch (failure.Field)
        {
            case PortfolioPhotoField.File:
                FileErrorText = failure.Message;
                break;
            case PortfolioPhotoField.Description:
                CaptionErrorText = failure.Message;
                break;
            default:
                ErrorText = failure.Message;
                break;
        }
    }

    /// <summary>
    /// Сбрасывает причины прошлой попытки перед новой.
    /// </summary>
    private void ClearErrors()
    {
        ErrorText = null;
        FileErrorText = null;
        CaptionErrorText = null;
    }

    /// <summary>
    /// Сообщает обо всех вычисляемых полях после смены режима или фотографии.
    /// </summary>
    private void NotifyAll()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(BarberProfileId));
        OnPropertyChanged(nameof(Photo));
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(CaptionLength));
        OnPropertyChanged(nameof(CaptionCounterText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RejectionReason));
        OnPropertyChanged(nameof(ShowRejectionReason));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanEditCaption));
        OnPropertyChanged(nameof(CanTogglePublication));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(PublicationActionText));
        OnPropertyChanged(nameof(HasPickedPhoto));
        OnPropertyChanged(nameof(PickedPhotoBytes));
        OnPropertyChanged(nameof(PickedFileName));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsUploading));
        OnPropertyChanged(nameof(CanSubmitUpload));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(FileErrorText));
        OnPropertyChanged(nameof(CaptionErrorText));

        NotifyCommands();
    }

    /// <summary>
    /// Пересчитывает доступность команд: она зависит от режима, позиции и занятости.
    /// </summary>
    private void NotifyCommands()
    {
        PickPhotoCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        SaveCaptionCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        TogglePublicationCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Кадр заблокирован администратором: владелец может только удалить его.
    /// </summary>
    private bool IsBlocked => _photo?.IsBlocked == true;

    /// <summary>
    /// Выбор файла доступен, пока нет запроса.
    /// </summary>
    private bool CanPickPhoto() => !IsBusy;

    /// <summary>
    /// Загрузка доступна по тем же правилам, что и кнопка.
    /// </summary>
    private bool CanUpload() => CanSubmitUpload;

    /// <summary>
    /// Подпись сохраняется только у существующего кадра и не во время запроса.
    /// </summary>
    private bool CanSaveCaption() => CanEditCaption && !IsBusy;

    /// <summary>
    /// «Выше» доступна не на первом кадре и не во время запроса.
    /// </summary>
    private bool CanMoveUpPhoto() => CanMoveUp && !IsBusy;

    /// <summary>
    /// «Ниже» доступна не на последнем кадре и не во время запроса.
    /// </summary>
    private bool CanMoveDownPhoto() => CanMoveDown && !IsBusy;

    /// <summary>
    /// Публикация переключается только у одобренного кадра и не во время запроса.
    /// </summary>
    private bool CanTogglePublicationPhoto() => CanTogglePublication && !IsBusy;

    /// <summary>
    /// Удаление доступно у существующего кадра и не во время запроса.
    /// </summary>
    private bool CanDeletePhoto() => CanDelete && !IsBusy;
}
