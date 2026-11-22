using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Раздел «Салоны» кабинета мастера: привязанные места и основной салон.
/// </summary>
/// <remarks>
/// <para>
/// Список — рабочий, а не витрина: сервер отдаёт все привязки профиля, включая салон на
/// проверке и отклонённый, и основной первым. Порядок и признак «основной» не
/// пересобираются на клиенте: их решает домен, а экран показывает ответ как есть.
/// </para>
/// <para>
/// «Сделать основным» — мобильное дополнение к вебу: в браузере основной салон выбирают
/// только при привязке, а с телефона это одна кнопка на карточке. Как и привязка, это
/// запись, поэтому она не повторяется сама: после сбоя исход неизвестен, и человек сам
/// решает, повторить ли действие.
/// </para>
/// <para>
/// Поиск нового места и привязка живут на отдельной странице
/// (<see cref="BarberSalonAddViewModel"/>): у раздела остаётся прочитанный список и смена
/// основного. Поэтому у раздела больше нет ни каталога, ни отложенных запросов, а вместе с
/// ними исчезла и подменяемая пауза тестового конструктора: подменять больше нечего.
/// </para>
/// </remarks>
public sealed partial class BarberSalonsViewModel : ObservableObject, IBarberCabinetSection
{
    private static string LoadFailedText => Loc.T("Не удалось загрузить салоны.");
    private static string PrimaryFailedText => Loc.T("Не удалось изменить основной салон.");
    private static string PrimaryChangedText => Loc.T("Основной салон изменён.");
    private static string EmptyTitleText => Loc.T("Салон не добавлен");
    private static string EmptyDescriptionText => Loc.T("Без салона расписание и запись не работают.");

    private readonly IBarberProfileApi _profileApi;
    private readonly AutoRetryLoop _salonsAutoRetry;

    private Guid _profileId = Guid.Empty;
    private bool _isActive;
    private bool _isActivating;
    private bool _hasLoaded;
    private CancellationTokenSource? _loadCts;
    private int _generation;
    private ApiFailureKind _salonsFailureKind;

    /// <summary>
    /// Создаёт раздел салонов.
    /// </summary>
    /// <param name="profileApi">Список привязок и назначение основного.</param>
    /// <param name="salonsAutoRetry">
    /// Цикл повтора чтения салонов; <c>null</c> — собственный цикл.
    /// </param>
    public BarberSalonsViewModel(
        IBarberProfileApi profileApi,
        AutoRetryLoop? salonsAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(profileApi);

        _profileApi = profileApi;

        _salonsAutoRetry = salonsAutoRetry ?? new AutoRetryLoop();
        _salonsAutoRetry.StateChanged += OnSalonsAutoRetryStateChanged;
    }

    /// <inheritdoc />
    public Guid? BarberProfileId => _profileId == Guid.Empty ? null : _profileId;

    /// <summary>
    /// Привязанные салоны в порядке сервера: основной первым.
    /// </summary>
    public ObservableCollection<BarberSalonItem> Salons { get; } = [];

    /// <summary>
    /// Идёт первое чтение списка: на экране индикатор.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт обновление свайпом: индикатор держит <c>RefreshView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Причина неудачи чтения списка; <c>null</c>, когда читать удалось.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет упавшее чтение списка.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Объявление после успешной привязки или смены основного.
    /// </summary>
    [ObservableProperty]
    private string? _announcement;

    /// <summary>
    /// Идёт назначение основного салона: кнопки не нажимаются дважды.
    /// </summary>
    [ObservableProperty]
    private bool _isSettingPrimary;

    /// <summary>
    /// Причина неудачи смены основного салона.
    /// </summary>
    [ObservableProperty]
    private string? _actionError;

    /// <summary>
    /// Список уже читался: первое чтение закончилось успешно хотя бы раз.
    /// </summary>
    public bool HasLoaded => _hasLoaded;

    /// <summary>
    /// В профиле есть привязанные салоны.
    /// </summary>
    public bool HasSalons => Salons.Count > 0;

    /// <summary>
    /// Показывать список салонов.
    /// </summary>
    public bool ShowContent => !IsFirstLoading && Salons.Count > 0;

    /// <summary>
    /// Показывать пустое состояние: читать удалось, но салонов нет.
    /// </summary>
    public bool ShowEmpty => !IsFirstLoading && _hasLoaded
        && ErrorMessage is null && Salons.Count == 0;

    /// <summary>
    /// Показывать карточку ошибки: салонов нет и читать не удалось.
    /// </summary>
    public bool ShowError => !IsFirstLoading && ErrorMessage is not null && Salons.Count == 0;

    /// <summary>
    /// Заголовок пустого состояния.
    /// </summary>
    public string EmptyTitle => Salons.Count == 0 ? EmptyTitleText : string.Empty;

    /// <summary>
    /// Подпись пустого состояния.
    /// </summary>
    public string EmptyDescription => Salons.Count == 0 ? EmptyDescriptionText : string.Empty;

    /// <summary>
    /// Есть баннер о результате.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Есть ошибка смены основного салона.
    /// </summary>
    public bool HasActionError => ActionError is not null;

    /// <summary>
    /// Цикл повтора списка: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop SalonsAutoRetry => _salonsAutoRetry;

    /// <summary>
    /// Завершается, когда цикл списка останавливается.
    /// </summary>
    internal Task SalonsRetryCompletion => _salonsAutoRetry.Completion;

    /// <inheritdoc />
    public void Initialize(Guid barberProfileId)
    {
        if (_profileId == barberProfileId)
        {
            OnPropertyChanged(nameof(BarberProfileId));

            return;
        }

        _profileId = barberProfileId;

        _salonsAutoRetry.Stop();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _generation++;

        Salons.Clear();

        _hasLoaded = false;
        _salonsFailureKind = ApiFailureKind.None;

        IsFirstLoading = false;
        IsRefreshing = false;
        ErrorMessage = null;
        IsAutoRetrying = false;
        Announcement = null;
        IsSettingPrimary = false;
        ActionError = null;

        NotifySalonState();
        OnPropertyChanged(nameof(BarberProfileId));
    }

    /// <inheritdoc />
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (_profileId == Guid.Empty)
        {
            return;
        }

        _isActive = true;

        if (_isActivating)
        {
            return;
        }

        _isActivating = true;

        var silent = _hasLoaded;

        if (silent)
        {
            IsRefreshing = true;
        }

        try
        {
            await LoadSalonsAsync(silent, cancellationToken);
        }
        finally
        {
            _isActivating = false;
            IsRefreshing = false;
        }

        TryStartSalonsAutoRetry();
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        _isActive = false;

        _salonsAutoRetry.Stop();

        IsAutoRetrying = false;
    }

    /// <summary>
    /// Сообщает, что раздел виден, и запускает автоповтор упавшего чтения.
    /// </summary>
    public void StartAutoRetry()
    {
        _isActive = true;

        TryStartSalonsAutoRetry();
    }

    /// <summary>
    /// Останавливает автоповтор, не сбрасывая показанные данные.
    /// </summary>
    public void StopAutoRetry() => Deactivate();

    /// <summary>
    /// Перечитывает список салонов по кнопке — сразу, без паузы цикла.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        if (_profileId == Guid.Empty || IsFirstLoading)
        {
            return;
        }

        _salonsAutoRetry.Stop();

        await LoadSalonsAsync(Salons.Count > 0, cancellationToken);

        TryStartSalonsAutoRetry();
    }

    /// <summary>
    /// Обновляет список свайпом.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsFirstLoading || IsRefreshing || _profileId == Guid.Empty)
        {
            IsRefreshing = false;

            return;
        }

        IsRefreshing = true;
        _salonsAutoRetry.Stop();

        try
        {
            await LoadSalonsAsync(silent: true, cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
        }

        TryStartSalonsAutoRetry();
    }

    /// <summary>
    /// Назначает салон основным: сервер сам снимает признак с прежнего.
    /// </summary>
    /// <param name="item">Строка салона из списка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SetPrimaryAsync(
        BarberSalonItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null
            || item.IsPrimary
            || IsSettingPrimary
            || _profileId == Guid.Empty)
        {
            return;
        }

        IsSettingPrimary = true;
        ActionError = null;

        try
        {
            await _profileApi.SetPrimarySalonAsync(
                _profileId,
                item.SalonId,
                cancellationToken);

            Announcement = PrimaryChangedText;

            await LoadSalonsAsync(silent: true, CancellationToken.None);
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            // Привязку убрали с другого устройства: строка устарела, список надо перечитать.
            ActionError = BarberSalonText.WriteError(exception, PrimaryFailedText);

            await LoadSalonsAsync(silent: true, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран ушёл: ошибку показывать некому.
        }
        catch (Exception exception)
        {
            ActionError = BarberSalonText.WriteError(exception, PrimaryFailedText);
        }
        finally
        {
            IsSettingPrimary = false;
        }
    }

    /// <summary>
    /// Убирает баннер о результате.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Убирает ошибку смены основного салона.
    /// </summary>
    [RelayCommand]
    private void DismissActionError() => ActionError = null;

    /// <summary>
    /// Читает список привязанных салонов.
    /// </summary>
    /// <param name="silent">Обновление уже показанного списка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadSalonsAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var cts = StartRequest(cancellationToken);

        // Тихая попытка могла отменить ещё не закончившееся первое чтение: индикатор
        // первого чтения принадлежит отменённому запросу, и оставить его включённым значит
        // навсегда показать пустой экран со спиннером.
        if (silent)
        {
            IsFirstLoading = false;
        }

        if (!silent)
        {
            IsFirstLoading = true;
            ErrorMessage = null;
        }

        var kind = ApiFailureKind.None;

        try
        {
            var salons = await _profileApi.GetSalonsAsync(_profileId, cts.Token);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Salons.Clear();

            foreach (var salon in salons)
            {
                Salons.Add(new BarberSalonItem(salon));
            }

            _hasLoaded = true;
            ErrorMessage = null;
            _salonsFailureKind = ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, cts.Token);

            if (kind != ApiFailureKind.None)
            {
                _salonsFailureKind = kind;

                // Тихий повтор не стирает уже показанные салоны: причина нужна только
                // пустому списку, где она объясняет отсутствие данных.
                if (Salons.Count == 0)
                {
                    ErrorMessage = BarberProfileText.ReadError(
                        exception,
                        kind,
                        LoadFailedText);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsFirstLoading = false;
            }

            cts.Dispose();
            NotifySalonState();
        }

        if (kind.IsRetryable())
        {
            TryStartSalonsAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Запускает цикл повтора списка, если раздел виден и сбой повторяемый.
    /// </summary>
    private void TryStartSalonsAutoRetry()
    {
        if (!_isActive
            || _profileId == Guid.Empty
            || !_salonsFailureKind.IsRetryable())
        {
            return;
        }

        _salonsAutoRetry.Start(RetrySalonsAsync);
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает список, не сбрасывая показанное и причину.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetrySalonsAsync(CancellationToken cancellationToken) =>
        LoadSalonsAsync(silent: true, cancellationToken);

    /// <summary>
    /// Начинает новое чтение, отменяя предыдущее.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены экрана.</param>
    /// <returns>Источник с связанным токеном.</returns>
    private CancellationTokenSource StartRequest(CancellationToken cancellationToken)
    {
        _loadCts?.Cancel();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _loadCts = cts;

        return cts;
    }

    /// <summary>
    /// Переключает строку «Пробуем снова…» списка вслед за циклом.
    /// </summary>
    private void OnSalonsAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _salonsAutoRetry.IsRunning;

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от списка салонов.
    /// </summary>
    private void NotifySalonState()
    {
        OnPropertyChanged(nameof(HasLoaded));
        OnPropertyChanged(nameof(HasSalons));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    /// <summary>
    /// Первое чтение закончилось: переключаются «список», «пусто» и «ошибка».
    /// </summary>
    partial void OnIsFirstLoadingChanged(bool value) => NotifySalonState();

    /// <summary>
    /// Причина чтения изменилась: переключаются «список» и «ошибка».
    /// </summary>
    partial void OnErrorMessageChanged(string? value) => NotifySalonState();

    /// <summary>
    /// Баннер появился или исчез.
    /// </summary>
    partial void OnAnnouncementChanged(string? value) =>
        OnPropertyChanged(nameof(HasAnnouncement));

    /// <summary>
    /// Ошибка смены основного появилась или исчезла.
    /// </summary>
    partial void OnActionErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasActionError));
}
