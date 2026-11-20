using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Раздел «Расписание» кабинета мастера: недельный шаблон, исключения на даты и отпуска.
/// </summary>
/// <remarks>
/// <para>
/// Это один из четырёх разделов <c>BarberCabinetPage</c>, поэтому у него нет собственного
/// жизненного цикла страницы: <see cref="ActivateAsync"/> зовёт страница, когда раздел стал
/// видимым, а <see cref="Deactivate"/> — перед переходом на дочерний экран. Первое чтение
/// показывает индикатор, последующие — тихое обновление: данные могли измениться, пока
/// мастер был на другой странице.
/// </para>
/// <para>
/// Время недели и исключений — локальное время салона, его нельзя пересчитывать в пояс
/// телефона. Отпуска, наоборот, передаются UTC-инстантами: пользователь вводит локальные даты
/// включительно, а запрос получает исключающую верхнюю границу — «с 15 по 17» превращается
/// в «15 00:00» … «18 00:00».
/// </para>
/// <para>
/// Сохранение недели отправляет ровно семь дней одним <c>PUT</c>: сервер проверяет полноту,
/// повторы и границы, поэтому частично сохранённого графика не бывает. Выключенный день
/// уходит с 10:00–20:00, а не с полуночью: сервер время выходного игнорирует, но осмысленное
/// значение нужно самому экрану, если день включат обратно.
/// </para>
/// <para>
/// Конфликт <c>409 barberSchedule.conflict</c> — не ошибка, а вопрос: сервер отказывается
/// сохранять правку, пока активные визиты выходят за новые часы. Диалог один на все записи
/// (неделя, исключение, отпуск): он запоминает действие, которое надо повторить с
/// <c>force</c>, поэтому подтверждение отправляет ровно тот же запрос, а не собранный заново.
/// </para>
/// </remarks>
public sealed partial class BarberScheduleViewModel : ObservableObject, IBarberCabinetSection
{
    /// <summary>
    /// Сколько затронутых визитов показывается в диалоге конфликта.
    /// </summary>
    /// <remarks>
    /// Список — объяснение, а не рабочий экран: двадцати строк хватает, чтобы понять масштаб,
    /// а остальные мастер увидит в разделе «Записи». Ограничение ещё и бережёт длину диалога
    /// на маленьком экране.
    /// </remarks>
    public const int MaxConflictItems = 20;

    /// <summary>
    /// Порядок дней в редакторе: неделя начинается с понедельника.
    /// </summary>
    /// <remarks>
    /// <see cref="Enum.GetValues{TEnum}"/> для <see cref="DayOfWeek"/> начинается с воскресенья,
    /// а привычный русский календарь — с понедельника; порядок задаётся явно.
    /// </remarks>
    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    private static string ReadFailedValue => Loc.T("Не удалось загрузить расписание.");
    private static string SaveFailedValue => Loc.T("Не удалось сохранить расписание.");
    private static string AddExceptionFailedValue => Loc.T("Не удалось добавить исключение.");
    private static string RemoveExceptionFailedValue => Loc.T("Не удалось удалить исключение.");
    private static string AddTimeOffFailedValue => Loc.T("Не удалось добавить отпуск.");
    private static string RemoveTimeOffFailedValue => Loc.T("Не удалось удалить отпуск.");

    private readonly IBarberScheduleApi _api;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _readAutoRetry;

    private Guid _profileId = Guid.Empty;
    private BarberScheduleResponse? _schedule;
    private string? _originalSignature;
    private bool _isRebuilding;
    private bool _isActive;
    private Guid? _pendingSalonId;
    private CancellationTokenSource? _loadCts;
    private int _generation;
    private ApiFailureKind _lastFailureKind;
    private Func<Task>? _pendingWrite;

    /// <summary>
    /// Создаёт раздел расписания.
    /// </summary>
    /// <remarks>
    /// <paramref name="readAutoRetry"/> необязателен, чтобы существующие вызовы и тесты не
    /// менялись. Тест подменяет паузы цикла, иначе проверка повтора ждала бы реальные секунды.
    /// </remarks>
    /// <param name="api">Клиент расписания мастера.</param>
    /// <param name="timeProvider">Часы: «сегодня» в поясе салона зависит от момента.</param>
    /// <param name="readAutoRetry">Цикл повтора чтения; <c>null</c> — обычное расписание пауз.</param>
    public BarberScheduleViewModel(
        IBarberScheduleApi api,
        TimeProvider timeProvider,
        AutoRetryLoop? readAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _api = api;
        _timeProvider = timeProvider;
        _readAutoRetry = readAutoRetry ?? new AutoRetryLoop();
        _readAutoRetry.StateChanged += OnReadAutoRetryStateChanged;
    }

    /// <inheritdoc />
    public Guid? BarberProfileId => _profileId == Guid.Empty ? null : _profileId;

    /// <summary>
    /// Салоны профиля; первый в списке — основной, как отдаёт сервер.
    /// </summary>
    public ObservableCollection<BarberScheduleSalonItem> Salons { get; } = [];

    /// <summary>
    /// Недельный шаблон выбранного салона: всегда семь дней, понедельник первый.
    /// </summary>
    public ObservableCollection<BarberScheduleDayItem> Days { get; } = [];

    /// <summary>
    /// Исключения выбранного салона в окне сервера, по возрастанию даты.
    /// </summary>
    public ObservableCollection<BarberScheduleExceptionItem> Exceptions { get; } = [];

    /// <summary>
    /// Отпуска профиля: они общие для всех салонов.
    /// </summary>
    public ObservableCollection<BarberTimeOffItem> TimeOffs { get; } = [];

    /// <summary>
    /// Затронутые визиты в диалоге конфликта, не больше <see cref="MaxConflictItems"/>.
    /// </summary>
    public ObservableCollection<ScheduleConflictItem> Conflicts { get; } = [];

    /// <summary>
    /// Выбранный салон; <c>null</c>, если салонов нет.
    /// </summary>
    [ObservableProperty]
    private BarberScheduleSalonItem? _selectedSalon;

    /// <summary>
    /// Идёт первое чтение: на экране индикатор вместо недели.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт тихое обновление уже показанных данных.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Причина неудачи чтения; <c>null</c>, когда читать удалось.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет упавшее чтение: под причиной видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Идёт запись расписания или связанной правки.
    /// </summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Причина неудачи сохранения недели; не путать с <see cref="ConflictError"/>.
    /// </summary>
    [ObservableProperty]
    private string? _saveError;

    /// <summary>
    /// Объявление после успешной правки: «Расписание сохранено.».
    /// </summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <summary>
    /// Диалог подтверждения конфликта открыт.
    /// </summary>
    [ObservableProperty]
    private bool _isConflictPromptVisible;

    /// <summary>
    /// Пояснение в диалоге конфликта: сколько записей и что с ними будет.
    /// </summary>
    [ObservableProperty]
    private string _conflictDescription = string.Empty;

    /// <summary>
    /// Причина неудачи повторной записи с <c>force</c>; показывается в диалоге.
    /// </summary>
    [ObservableProperty]
    private string? _conflictError;

    /// <summary>
    /// В ответе было больше визитов, чем поместилось в <see cref="Conflicts"/>.
    /// </summary>
    [ObservableProperty]
    private bool _hasMoreConflicts;

    /// <summary>
    /// Дата нового исключения для <c>DatePicker</c>; по умолчанию сегодня в поясе салона.
    /// </summary>
    [ObservableProperty]
    private DateTime _newExceptionDate;

    /// <summary>
    /// Новое исключение — выходной; иначе свои часы.
    /// </summary>
    [ObservableProperty]
    private bool _newExceptionIsDayOff = true;

    /// <summary>
    /// Начало своих часов нового исключения.
    /// </summary>
    [ObservableProperty]
    private TimeSpan _newExceptionStartTime = BarberScheduleDayItem.DefaultStartTime;

    /// <summary>
    /// Окончание своих часов нового исключения.
    /// </summary>
    [ObservableProperty]
    private TimeSpan _newExceptionEndTime = TimeSpan.FromHours(14);

    /// <summary>
    /// Пометка нового исключения; <c>null</c> на сервер не уходит, пустая строка — тоже.
    /// </summary>
    [ObservableProperty]
    private string _newExceptionReason = string.Empty;

    /// <summary>
    /// Причина неудачи добавления или удаления исключения.
    /// </summary>
    [ObservableProperty]
    private string? _exceptionError;

    /// <summary>
    /// Начало нового отпуска: локальная дата включительно.
    /// </summary>
    [ObservableProperty]
    private DateTime _newTimeOffStartDate;

    /// <summary>
    /// Конец нового отпуска: локальная дата включительно.
    /// </summary>
    [ObservableProperty]
    private DateTime _newTimeOffEndDate;

    /// <summary>
    /// Пометка нового отпуска.
    /// </summary>
    [ObservableProperty]
    private string _newTimeOffReason = string.Empty;

    /// <summary>
    /// Причина неудачи добавления или удаления отпуска.
    /// </summary>
    [ObservableProperty]
    private string? _timeOffError;

    /// <summary>
    /// Показывать неделю: первое чтение закончилось успешно.
    /// </summary>
    public bool ShowContent => !IsFirstLoading && _schedule is not null;

    /// <summary>
    /// Показывать блок ошибки: читать оказалось нечего.
    /// </summary>
    public bool ShowError => !IsFirstLoading && ErrorMessage is not null && _schedule is null;

    /// <summary>
    /// У профиля больше одного салона: нужен выбор салона.
    /// </summary>
    public bool HasMultipleSalons => Salons.Count > 1;

    /// <summary>
    /// У профиля нет ни одного салона: правка расписания невозможна.
    /// </summary>
    public bool HasNoSalons => Salons.Count == 0;

    /// <summary>
    /// Подпись единственного салона вместо списка выбора.
    /// </summary>
    public string SingleSalonText => SelectedSalon?.DisplayText ?? string.Empty;

    /// <summary>
    /// Неделя отличается от загруженной: кнопка сохранения активна.
    /// </summary>
    public bool IsDirty =>
        _originalSignature is not null
        && !string.Equals(CurrentSignature(), _originalSignature, StringComparison.Ordinal);

    /// <summary>
    /// Есть рабочий день, который кончается не позже начала.
    /// </summary>
    public bool HasInvalidHours => Days.Any(day => day.IsInvalid);

    /// <summary>
    /// Все семь дней отмечены выходными: клиенты не увидят свободное время.
    /// </summary>
    public bool AllDaysOff => Days.Count == WEEK_LENGTH && Days.All(day => day.IsDayOff);

    /// <summary>
    /// Надпись на кнопке сохранения недели.
    /// </summary>
    public string SaveButtonText => IsSaving
        ? Loc.T("Сохраняем…")
        : Loc.T("Сохранить расписание");

    /// <summary>
    /// Заголовок диалога конфликта.
    /// </summary>
    public string ConflictTitle => Conflicts.Count > 0
        ? Loc.T("Записи вне новых часов")
        : string.Empty;

    /// <summary>
    /// Видна ли строка «показаны не все записи».
    /// </summary>
    public string MoreConflictsText => HasMoreConflicts
        ? Loc.T("Показаны не все записи: откройте раздел «Записи», чтобы увидеть остальные.")
        : string.Empty;

    /// <summary>
    /// Есть ли исключения на даты.
    /// </summary>
    public bool HasExceptions => Exceptions.Count > 0;

    /// <summary>
    /// Есть ли отпуска.
    /// </summary>
    public bool HasTimeOffs => TimeOffs.Count > 0;

    /// <summary>
    /// Есть ли объявление для баннера.
    /// </summary>
    public bool HasAnnouncement => Announcement.Length > 0;

    /// <summary>
    /// Минимальная дата исключения: сегодня в поясе салона.
    /// </summary>
    public DateTime ExceptionMinDate => SalonToday.ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Максимальная дата исключения: сегодня + 90 дней.
    /// </summary>
    public DateTime ExceptionMaxDate =>
        SalonToday.AddDays(90).ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Минимальная дата отпуска: сегодня в поясе салона.
    /// </summary>
    public DateTime TimeOffMinDate => SalonToday.ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Максимальная дата отпуска: сегодня + 365 дней.
    /// </summary>
    public DateTime TimeOffMaxDate =>
        SalonToday.AddDays(365).ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Цикл повтора чтения: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop ReadAutoRetry => _readAutoRetry;

    /// <summary>
    /// Завершается, когда цикл повтора чтения останавливается.
    /// </summary>
    internal Task ReadRetryCompletion => _readAutoRetry.Completion;

    /// <summary>
    /// Сколько дней в неделе: ровно столько приходит и уходит на сервер.
    /// </summary>
    private const int WEEK_LENGTH = 7;

    /// <inheritdoc />
    public void Initialize(Guid barberProfileId)
    {
        _profileId = barberProfileId;

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

        var silent = _schedule is not null;

        if (silent)
        {
            IsRefreshing = true;
        }

        await LoadAsync(silent, cancellationToken);
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        _isActive = false;
        _readAutoRetry.Stop();
    }

    /// <summary>
    /// Выбирает салон раздела; если расписание ещё не загружено, выбор применится после чтения.
    /// </summary>
    /// <remarks>
    /// Нужен переходу из «Моих салонов»: страница переключает раздел и просит показать
    /// конкретный салон. Неизвестный идентификатор игнорируется — список салонов мог
    /// измениться, и падать из-за этого незачем.
    /// </remarks>
    /// <param name="salonId">Салон, который нужно показать.</param>
    public void SelectSalon(Guid salonId)
    {
        _pendingSalonId = salonId;

        ApplyPendingSalon();
    }

    /// <summary>
    /// Сообщает, что раздел виден, и запускает автоповтор упавшего чтения.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: без повторяемого сбоя или уже работающего цикла ничего не делает.
    /// Запуск и остановка обязаны идти из UI-потока — <see cref="AutoRetryLoop.StateChanged"/>
    /// синхронно меняет привязанное <see cref="IsAutoRetrying"/>.
    /// </remarks>
    public void StartAutoRetry()
    {
        _isActive = true;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Останавливает автоповтор чтения, не сбрасывая показанные данные.
    /// </summary>
    public void StopAutoRetry()
    {
        _isActive = false;
        _readAutoRetry.Stop();
    }

    /// <summary>
    /// Повторяет чтение по кнопке — сразу, без паузы цикла.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        if (_profileId == Guid.Empty || IsFirstLoading)
        {
            return;
        }

        _readAutoRetry.Stop();

        await LoadAsync(silent: _schedule is not null, cancellationToken);
    }

    /// <summary>
    /// Сохраняет неделю; при конфликте открывает диалог подтверждения.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => SaveCoreAsync(force: false);

    /// <summary>
    /// Подтверждает конфликт и повторяет ровно ту же запись с <c>force</c>.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmForceAsync()
    {
        if (_pendingWrite is not { } write || IsSaving)
        {
            return;
        }

        ConflictError = null;

        await write();
    }

    /// <summary>
    /// Закрывает диалог конфликта, ничего не отправляя.
    /// </summary>
    [RelayCommand]
    private void CancelConflict() => CloseConflictPrompt();

    /// <summary>
    /// Заполняет будни 10:00–20:00, выходные оставляет выходными.
    /// </summary>
    [RelayCommand]
    private void FillWeekdays()
    {
        if (IsSaving)
        {
            return;
        }

        foreach (var day in Days)
        {
            day.StartTime = BarberScheduleDayItem.DefaultStartTime;
            day.EndTime = BarberScheduleDayItem.DefaultEndTime;
            day.IsDayOff = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        }

        SaveError = null;

        NotifyDerivedState();
    }

    /// <summary>
    /// Возвращает неделю к загруженной, отбрасывая несохранённые правки.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private void DiscardChanges()
    {
        if (IsSaving)
        {
            return;
        }

        RebuildSalon();
    }

    /// <summary>
    /// Добавляет исключение на дату; при конфликте открывает диалог подтверждения.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddException))]
    private Task AddExceptionAsync() => AddExceptionCoreAsync(force: false);

    /// <summary>
    /// Снимает исключение с даты.
    /// </summary>
    /// <param name="item">Строка исключения из списка.</param>
    [RelayCommand]
    private async Task RemoveExceptionAsync(BarberScheduleExceptionItem? item)
    {
        if (item is null || SelectedSalon is not { } salon || IsSaving)
        {
            return;
        }

        IsSaving = true;
        ExceptionError = null;

        try
        {
            await _api.RemoveScheduleExceptionAsync(_profileId, salon.SalonId, item.Date);

            Announcement = Loc.T("Исключение удалено.");

            await LoadAsync(silent: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            var failure = BarberScheduleText.WriteError(
                exception,
                RemoveExceptionFailedValue,
                "удалить исключение");

            if (exception is BarberSearchApiException
                {
                    StatusCode: HttpStatusCode.NotFound,
                })
            {
                // Исключение уже сняли с другого устройства: это не сбой, но состояние
                // на экране устарело — перечитываем расписание и говорим об этом. Причина
                // ставится после чтения: пересборка недели сбрасывает ошибки раздела,
                // и выставленная до неё строка исчезла бы, не дойдя до человека.
                await LoadAsync(silent: true, CancellationToken.None);

                ExceptionError = Loc.T("Это исключение уже удалено.");
            }
            else
            {
                ExceptionError = failure.Message;
            }
        }
        finally
        {
            IsSaving = false;
            NotifyDerivedState();
        }
    }

    /// <summary>
    /// Добавляет отпуск; даты вводятся включительно, на сервер уходит исключающий конец.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddTimeOff))]
    private Task AddTimeOffAsync() => AddTimeOffCoreAsync(force: false);

    /// <summary>
    /// Удаляет отпуск.
    /// </summary>
    /// <param name="item">Строка отпуска из списка.</param>
    [RelayCommand]
    private async Task RemoveTimeOffAsync(BarberTimeOffItem? item)
    {
        if (item is null || IsSaving)
        {
            return;
        }

        IsSaving = true;
        TimeOffError = null;

        try
        {
            await _api.RemoveTimeOffAsync(_profileId, item.Id);

            Announcement = Loc.T("Отпуск удалён.");

            await LoadAsync(silent: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            if (exception is BarberSearchApiException
                {
                    StatusCode: HttpStatusCode.NotFound,
                })
            {
                // Отпуск уже удалён с другого устройства: перечитываем список и только
                // потом объясняем причину — пересборка раздела сбрасывает ошибку отпуска.
                await LoadAsync(silent: true, CancellationToken.None);

                TimeOffError = Loc.T("Этот отпуск уже удалён.");
            }
            else
            {
                TimeOffError = BarberScheduleText.WriteError(
                    exception,
                    RemoveTimeOffFailedValue,
                    "удалить отпуск").Message;
            }
        }
        finally
        {
            IsSaving = false;
            NotifyDerivedState();
        }
    }

    /// <summary>
    /// Убирает объявление после успешной правки.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = string.Empty;

    /// <summary>
    /// Часы работы в поясе салона; неизвестный пояс — UTC.
    /// </summary>
    private TimeZoneInfo? SalonTimeZone =>
        SelectedSalon?.TimeZone ?? Salons.FirstOrDefault(salon => salon.IsPrimary)?.TimeZone;

    /// <summary>
    /// Сегодняшняя дата в поясе салона: границы дат зависят от неё.
    /// </summary>
    private DateOnly SalonToday =>
        SalonClock.Today(SalonTimeZone, _timeProvider.GetUtcNow());

    /// <summary>
    /// Выбранный салон из ответа сервера: неделя и исключения.
    /// </summary>
    private BarberScheduleSalonResponse? SelectedSalonSource =>
        _schedule?.Salons.FirstOrDefault(
            salon => salon.SalonId == SelectedSalon?.SalonId);

    /// <summary>
    /// Подпись недели для сравнения с загруженной.
    /// </summary>
    private string CurrentSignature() => string.Join(
        '|',
        Days.Select(day => string.Create(
            CultureInfo.InvariantCulture,
            $"{day.DayOfWeek}:{day.IsDayOff}:{(int)day.StartTime.TotalMinutes}:{(int)day.EndTime.TotalMinutes}")));

    /// <summary>
    /// Читает расписание; тихий режим не показывает индикатор первого чтения.
    /// </summary>
    /// <param name="silent">Обновление уже показанных данных.</param>
    /// <param name="cancellationToken">Токен отмены экрана.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var cts = StartRequest(cancellationToken);

        if (!silent)
        {
            IsFirstLoading = true;
            ErrorMessage = null;
        }

        var kind = ApiFailureKind.None;

        try
        {
            var schedule = await _api.GetAsync(_profileId, cancellationToken: cts.Token);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            _schedule = schedule;
            ErrorMessage = null;
            _lastFailureKind = ApiFailureKind.None;

            RebuildFromResponse();
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
                _lastFailureKind = kind;

                // Тихое обновление не имеет права стирать уже показанное: иначе возврат на
                // раздел при обрыве связи выглядел бы как «расписание исчезло».
                if (_schedule is null)
                {
                    ErrorMessage = BarberScheduleText.ReadError(exception, ReadFailedValue);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsFirstLoading = false;
                IsRefreshing = false;
            }

            cts.Dispose();

            NotifyDerivedState();
        }

        if (kind.IsRetryable())
        {
            TryStartAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Сохраняет неделю; <paramref name="force"/> повторяет запись после подтверждения.
    /// </summary>
    /// <param name="force">Сохранить, даже если визиты выйдут за новые часы.</param>
    /// <returns>Задача записи.</returns>
    private async Task SaveCoreAsync(bool force)
    {
        if (SelectedSalon is not { } salon || IsSaving || HasInvalidHours)
        {
            return;
        }

        if (!force && !IsDirty)
        {
            return;
        }

        IsSaving = true;
        SaveError = null;

        try
        {
            await _api.ReplaceWorkingSchedulesAsync(
                _profileId,
                salon.SalonId,
                BuildWeekRequest(),
                force);

            CloseConflictPrompt();

            Announcement = Loc.T("Расписание сохранено.");

            await LoadAsync(silent: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            var failure = BarberScheduleText.WriteError(
                exception,
                SaveFailedValue,
                "сохранить расписание");

            HandleWriteFailure(
                exception,
                failure,
                message => SaveError = message,
                () => SaveCoreAsync(force: true));
        }
        finally
        {
            IsSaving = false;
            NotifyDerivedState();
        }
    }

    /// <summary>
    /// Добавляет исключение; <paramref name="force"/> повторяет запись после подтверждения.
    /// </summary>
    /// <param name="force">Сохранить, даже если визиты останутся вне расписания.</param>
    /// <returns>Задача записи.</returns>
    private async Task AddExceptionCoreAsync(bool force)
    {
        if (SelectedSalon is not { } salon || IsSaving)
        {
            return;
        }

        if (!ValidateExceptionInput(out var error))
        {
            ExceptionError = error;

            return;
        }

        ExceptionError = null;
        IsSaving = true;

        try
        {
            await _api.SetScheduleExceptionAsync(
                _profileId,
                salon.SalonId,
                DateOnly.FromDateTime(NewExceptionDate),
                BuildExceptionRequest(),
                force);

            CloseConflictPrompt();
            ResetExceptionForm();

            Announcement = Loc.T("Исключение добавлено.");

            await LoadAsync(silent: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            var failure = BarberScheduleText.WriteError(
                exception,
                AddExceptionFailedValue,
                "добавить исключение");

            HandleWriteFailure(
                exception,
                failure,
                message => ExceptionError = message,
                () => AddExceptionCoreAsync(force: true));
        }
        finally
        {
            IsSaving = false;
            NotifyDerivedState();
        }
    }

    /// <summary>
    /// Добавляет отпуск; <paramref name="force"/> повторяет запись после подтверждения.
    /// </summary>
    /// <param name="force">Сохранить, даже если визиты попадают в период.</param>
    /// <returns>Задача записи.</returns>
    private async Task AddTimeOffCoreAsync(bool force)
    {
        if (IsSaving)
        {
            return;
        }

        if (!ValidateTimeOffInput(out var error))
        {
            TimeOffError = error;

            return;
        }

        TimeOffError = null;
        IsSaving = true;

        try
        {
            await _api.AddTimeOffAsync(_profileId, BuildTimeOffRequest(), force);

            CloseConflictPrompt();
            ResetTimeOffForm();

            Announcement = Loc.T("Отпуск добавлен.");

            await LoadAsync(silent: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            var failure = BarberScheduleText.WriteError(
                exception,
                AddTimeOffFailedValue,
                "добавить отпуск");

            HandleWriteFailure(
                exception,
                failure,
                message => TimeOffError = message,
                () => AddTimeOffCoreAsync(force: true));
        }
        finally
        {
            IsSaving = false;
            NotifyDerivedState();
        }
    }

    /// <summary>
    /// Разбирает недельный шаблон ответа в строки редактора.
    /// </summary>
    private void RebuildFromResponse()
    {
        var previousId = SelectedSalon?.SalonId;

        _isRebuilding = true;
        Salons.Clear();

        if (_schedule is not null)
        {
            foreach (var salon in _schedule.Salons)
            {
                Salons.Add(new BarberScheduleSalonItem(salon));
            }
        }

        var selected = _pendingSalonId is { } pending
            ? Salons.FirstOrDefault(salon => salon.SalonId == pending)
            : null;

        selected ??= Salons.FirstOrDefault(salon => salon.SalonId == previousId);
        selected ??= Salons.FirstOrDefault(salon => salon.IsPrimary);
        selected ??= Salons.FirstOrDefault();

        SelectedSalon = selected;

        if (selected is not null && selected.SalonId == _pendingSalonId)
        {
            _pendingSalonId = null;
        }

        _isRebuilding = false;

        RebuildSalon();
    }

    /// <summary>
    /// Собирает неделю, исключения и отпуска выбранного салона.
    /// </summary>
    private void RebuildSalon()
    {
        foreach (var day in Days)
        {
            day.PropertyChanged -= OnDayChanged;
        }

        Days.Clear();
        Exceptions.Clear();

        var source = SelectedSalonSource;

        foreach (var dayOfWeek in WeekOrder)
        {
            var response = source?.Weekly.FirstOrDefault(day => day.DayOfWeek == dayOfWeek);
            var item = BarberScheduleDayItem.From(dayOfWeek, response);

            item.PropertyChanged += OnDayChanged;

            Days.Add(item);
        }

        if (source is not null)
        {
            foreach (var exception in source.Exceptions.OrderBy(exception => exception.Date))
            {
                Exceptions.Add(new BarberScheduleExceptionItem(exception));
            }
        }

        RebuildTimeOffs();
        ResetExceptionForm();
        ResetTimeOffForm();

        _originalSignature = CurrentSignature();

        SaveError = null;
        ExceptionError = null;
        TimeOffError = null;

        CloseConflictPrompt();

        OnPropertyChanged(nameof(HasExceptions));
        OnPropertyChanged(nameof(HasTimeOffs));

        NotifySalonState();
        NotifyDerivedState();
    }

    /// <summary>
    /// Пересобирает отпуска: их границы показываются в поясе выбранного салона.
    /// </summary>
    private void RebuildTimeOffs()
    {
        TimeOffs.Clear();

        if (_schedule is null)
        {
            return;
        }

        var timeZone = SalonTimeZone;

        foreach (var timeOff in _schedule.TimeOffs)
        {
            TimeOffs.Add(new BarberTimeOffItem(timeOff, timeZone));
        }
    }

    /// <summary>
    /// Применяет отложенный выбор салона, если он уже есть в списке.
    /// </summary>
    private void ApplyPendingSalon()
    {
        if (_pendingSalonId is not { } pending)
        {
            return;
        }

        var salon = Salons.FirstOrDefault(candidate => candidate.SalonId == pending);

        if (salon is null)
        {
            return;
        }

        _pendingSalonId = null;

        SelectedSalon = salon;
    }

    /// <summary>
    /// Сообщает об изменении дня недели: от него зависят «есть правки» и «невалидные часы».
    /// </summary>
    private void OnDayChanged(object? sender, PropertyChangedEventArgs e) => NotifyDerivedState();

    /// <summary>
    /// Разрешает сохранение: есть салон, правки и нет невалидных часов.
    /// </summary>
    private bool CanSave() =>
        SelectedSalon is not null
        && !IsFirstLoading
        && !IsSaving
        && IsDirty
        && !HasInvalidHours;

    /// <summary>
    /// Разрешает отмену правок: отменять нечего, если неделя совпадает с загруженной.
    /// </summary>
    private bool CanDiscardChanges() => !IsSaving && IsDirty;

    /// <summary>
    /// Разрешает добавление исключения: дата в окне, часы согласованы, пометка не длиннее 500.
    /// </summary>
    private bool CanAddException()
    {
        if (SelectedSalon is null || IsSaving)
        {
            return false;
        }

        if (NewExceptionReason.Trim().Length > BarberScheduleText.ReasonMaxLength)
        {
            return false;
        }

        if (!NewExceptionIsDayOff && NewExceptionEndTime <= NewExceptionStartTime)
        {
            return false;
        }

        var date = DateOnly.FromDateTime(NewExceptionDate);

        return date >= DateOnly.FromDateTime(ExceptionMinDate)
            && date <= DateOnly.FromDateTime(ExceptionMaxDate);
    }

    /// <summary>
    /// Разрешает добавление отпуска: даты в окне, конец не раньше начала.
    /// </summary>
    private bool CanAddTimeOff()
    {
        if (IsSaving)
        {
            return false;
        }

        if (NewTimeOffReason.Trim().Length > BarberScheduleText.ReasonMaxLength)
        {
            return false;
        }

        var start = DateOnly.FromDateTime(NewTimeOffStartDate);
        var end = DateOnly.FromDateTime(NewTimeOffEndDate);
        var min = DateOnly.FromDateTime(TimeOffMinDate);
        var max = DateOnly.FromDateTime(TimeOffMaxDate);

        return start >= min && end <= max && end >= start;
    }

    /// <summary>
    /// Проверяет ввод исключения и возвращает причину отказа.
    /// </summary>
    /// <param name="error">Текст ошибки; <c>null</c>, если ввод верен.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool ValidateExceptionInput(out string? error)
    {
        error = null;

        if (NewExceptionReason.Trim().Length > BarberScheduleText.ReasonMaxLength)
        {
            error = BarberScheduleText.ReasonTooLong;

            return false;
        }

        if (!NewExceptionIsDayOff && NewExceptionEndTime <= NewExceptionStartTime)
        {
            error = BarberScheduleText.InvalidWorkingHours;

            return false;
        }

        var date = DateOnly.FromDateTime(NewExceptionDate);

        if (date < DateOnly.FromDateTime(ExceptionMinDate)
            || date > DateOnly.FromDateTime(ExceptionMaxDate))
        {
            error = Loc.T("Дата должна быть в пределах 90 дней от сегодня.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Проверяет ввод отпуска и возвращает причину отказа.
    /// </summary>
    /// <param name="error">Текст ошибки; <c>null</c>, если ввод верен.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool ValidateTimeOffInput(out string? error)
    {
        error = null;

        if (NewTimeOffReason.Trim().Length > BarberScheduleText.ReasonMaxLength)
        {
            error = BarberScheduleText.ReasonTooLong;

            return false;
        }

        var start = DateOnly.FromDateTime(NewTimeOffStartDate);
        var end = DateOnly.FromDateTime(NewTimeOffEndDate);

        if (end < start)
        {
            error = BarberScheduleText.InvalidTimeOffPeriod;

            return false;
        }

        if (start < DateOnly.FromDateTime(TimeOffMinDate)
            || end > DateOnly.FromDateTime(TimeOffMaxDate))
        {
            error = Loc.T("Период должен быть в пределах года от сегодня.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Собирает тело недели: ровно семь дней, понедельник первый.
    /// </summary>
    /// <remarks>
    /// Выходной уходит с 10:00–20:00, а не с полуночью: сервер время выходного игнорирует,
    /// но такое значение остаётся осмысленным, если день включат обратно.
    /// </remarks>
    /// <returns>Тело <c>PUT .../working-schedules</c>.</returns>
    private ReplaceWorkingSchedulesRequest BuildWeekRequest()
    {
        var days = new List<ReplaceWorkingScheduleDayRequest>(WEEK_LENGTH);

        foreach (var dayOfWeek in WeekOrder)
        {
            var day = Days.First(item => item.DayOfWeek == dayOfWeek);

            days.Add(new ReplaceWorkingScheduleDayRequest(
                dayOfWeek,
                day.IsDayOff,
                day.IsDayOff
                    ? TimeOnly.FromTimeSpan(BarberScheduleDayItem.DefaultStartTime)
                    : BarberScheduleDayItem.ToTimeOnly(day.StartTime),
                day.IsDayOff
                    ? TimeOnly.FromTimeSpan(BarberScheduleDayItem.DefaultEndTime)
                    : BarberScheduleDayItem.ToTimeOnly(day.EndTime)));
        }

        return new ReplaceWorkingSchedulesRequest(days);
    }

    /// <summary>
    /// Собирает тело исключения: для выходного часы — <c>00:00</c>.
    /// </summary>
    /// <returns>Тело <c>PUT .../schedule-exceptions/{date}</c>.</returns>
    private SetScheduleExceptionRequest BuildExceptionRequest() =>
        new(
            NewExceptionIsDayOff,
            NewExceptionIsDayOff
                ? TimeOnly.MinValue
                : BarberScheduleDayItem.ToTimeOnly(NewExceptionStartTime),
            NewExceptionIsDayOff
                ? TimeOnly.MinValue
                : BarberScheduleDayItem.ToTimeOnly(NewExceptionEndTime),
            string.IsNullOrWhiteSpace(NewExceptionReason) ? null : NewExceptionReason.Trim());

    /// <summary>
    /// Собирает тело отпуска: конец — полночь дня после последнего включённого.
    /// </summary>
    /// <returns>Тело <c>POST .../time-offs</c>.</returns>
    private AddBarberProfileTimeOffRequest BuildTimeOffRequest()
    {
        var timeZone = SalonTimeZone;
        var startDate = DateOnly.FromDateTime(NewTimeOffStartDate);
        var endDate = DateOnly.FromDateTime(NewTimeOffEndDate);

        return new AddBarberProfileTimeOffRequest(
            LocalMidnightUtc(startDate, timeZone),
            LocalMidnightUtc(endDate.AddDays(1), timeZone),
            string.IsNullOrWhiteSpace(NewTimeOffReason) ? null : NewTimeOffReason.Trim());
    }

    /// <summary>
    /// Переводит локальную полночь даты салона в UTC-инстант.
    /// </summary>
    /// <remarks>
    /// Смещение считается через <see cref="TimeZoneInfo"/>, а не через пояс телефона: телефон
    /// может жить в другом городе. Если полуночи в этот день не было (перевод часов),
    /// берётся следующий существующий час — иначе конверсия бросила бы исключение, и отпуск
    /// нельзя было бы добавить.
    /// </remarks>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Тот же момент в UTC.</returns>
    private static DateTimeOffset LocalMidnightUtc(DateOnly date, TimeZoneInfo? timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (timeZone is null)
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }

        if (timeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);

        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    /// <summary>
    /// Показывает диалог конфликта и запоминает действие для повтора с <c>force</c>.
    /// </summary>
    /// <param name="conflicts">Затронутые визиты из ответа.</param>
    /// <param name="retry">Запись, которую надо повторить после подтверждения.</param>
    private void ShowConflictPrompt(
        IReadOnlyList<ScheduleConflictResponse> conflicts,
        Func<Task> retry)
    {
        Conflicts.Clear();

        foreach (var conflict in conflicts.Take(MaxConflictItems))
        {
            Conflicts.Add(new ScheduleConflictItem(conflict));
        }

        ConflictDescription = Loc.T(
            "{0} записей. Клиенты придут в назначенное время — визит можно отменить в разделе «Записи».",
            conflicts.Count);

        HasMoreConflicts = conflicts.Count > MaxConflictItems;
        ConflictError = null;
        _pendingWrite = retry;
        IsConflictPromptVisible = true;
        OnPropertyChanged(nameof(ConflictTitle));
    }

    /// <summary>
    /// Закрывает диалог конфликта и забывает отложенное действие.
    /// </summary>
    private void CloseConflictPrompt()
    {
        IsConflictPromptVisible = false;
        _pendingWrite = null;
        Conflicts.Clear();
        ConflictDescription = string.Empty;
        HasMoreConflicts = false;
        ConflictError = null;
        OnPropertyChanged(nameof(ConflictTitle));
    }

    /// <summary>
    /// Раскладывает неудачу записи: конфликт — в диалог, остальное — в причину раздела.
    /// </summary>
    /// <param name="exception">Исключение записи.</param>
    /// <param name="failure">Разобранная причина.</param>
    /// <param name="showPlain">Куда писать обычную ошибку раздела.</param>
    /// <param name="retry">Повтор записи с <c>force</c>.</param>
    private void HandleWriteFailure(
        Exception exception,
        ScheduleWriteFailure failure,
        Action<string> showPlain,
        Func<Task> retry)
    {
        if (failure.IsConflict
            && exception is BarberSearchApiException apiException
            && apiException.Conflicts.Count > 0)
        {
            ShowConflictPrompt(apiException.Conflicts, retry);

            return;
        }

        // Пустой список конфликтов — тоже конфликт, но объяснять нечем: повторная запись
        // вслепую ничего не изменит, поэтому человек видит причину, а не диалог.
        if (IsConflictPromptVisible)
        {
            ConflictError = failure.Message;
        }
        else
        {
            showPlain(failure.Message);
        }
    }

    /// <summary>
    /// Возвращает форму исключения в исходное состояние.
    /// </summary>
    private void ResetExceptionForm()
    {
        NewExceptionDate = ExceptionMinDate;
        NewExceptionIsDayOff = true;
        NewExceptionStartTime = BarberScheduleDayItem.DefaultStartTime;
        NewExceptionEndTime = TimeSpan.FromHours(14);
        NewExceptionReason = string.Empty;
    }

    /// <summary>
    /// Возвращает форму отпуска в исходное состояние.
    /// </summary>
    private void ResetTimeOffForm()
    {
        NewTimeOffStartDate = TimeOffMinDate;
        NewTimeOffEndDate = TimeOffMinDate;
        NewTimeOffReason = string.Empty;
    }

    /// <summary>
    /// Запускает цикл повтора, если раздел виден и сбой повторяемый.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isActive
            || _profileId == Guid.Empty
            || !_lastFailureKind.IsRetryable())
        {
            return;
        }

        _readAutoRetry.Start(RetryFailedReadAsync);
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает расписание, не сбрасывая показанное.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя для следующего решения цикла.</returns>
    private Task<ApiFailureKind> RetryFailedReadAsync(CancellationToken cancellationToken) =>
        LoadAsync(silent: _schedule is not null, cancellationToken);

    /// <summary>
    /// Начинает новое чтение, отменяя предыдущее.
    /// </summary>
    /// <remarks>
    /// Прежний источник только отменяется: освобождает его тот запрос, который его создал,
    /// в своём <c>finally</c> — иначе гонка с ещё живым <c>await</c> на его токене.
    /// </remarks>
    /// <param name="cancellationToken">Токен экрана.</param>
    /// <returns>Источник с связанным токеном.</returns>
    private CancellationTokenSource StartRequest(CancellationToken cancellationToken)
    {
        _loadCts?.Cancel();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _loadCts = cts;

        return cts;
    }

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnReadAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _readAutoRetry.IsRunning;

    /// <summary>
    /// Сообщает об изменении всего, что зависит от недели и правок.
    /// </summary>
    private void NotifyDerivedState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasInvalidHours));
        OnPropertyChanged(nameof(AllDaysOff));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));

        SaveCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        AddExceptionCommand.NotifyCanExecuteChanged();
        AddTimeOffCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Сообщает об изменении списка салонов, выбранного салона и границ дат.
    /// </summary>
    private void NotifySalonState()
    {
        OnPropertyChanged(nameof(HasMultipleSalons));
        OnPropertyChanged(nameof(HasNoSalons));
        OnPropertyChanged(nameof(SingleSalonText));
        OnPropertyChanged(nameof(ExceptionMinDate));
        OnPropertyChanged(nameof(ExceptionMaxDate));
        OnPropertyChanged(nameof(TimeOffMinDate));
        OnPropertyChanged(nameof(TimeOffMaxDate));

        NewExceptionDate = ClampDate(NewExceptionDate, ExceptionMinDate, ExceptionMaxDate);
        NewTimeOffStartDate = ClampDate(NewTimeOffStartDate, TimeOffMinDate, TimeOffMaxDate);
        NewTimeOffEndDate = ClampDate(NewTimeOffEndDate, NewTimeOffStartDate, TimeOffMaxDate);

        AddExceptionCommand.NotifyCanExecuteChanged();
        AddTimeOffCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Зажимает дату в допустимый диапазон.
    /// </summary>
    /// <param name="value">Дата из формы.</param>
    /// <param name="min">Минимальная дата.</param>
    /// <param name="max">Максимальная дата.</param>
    /// <returns>Дата в диапазоне, без времени суток.</returns>
    private static DateTime ClampDate(DateTime value, DateTime min, DateTime max)
    {
        var date = value.Date;

        if (date < min)
        {
            return min;
        }

        return date > max ? max : date;
    }

    /// <summary>
    /// Приводит время формы к суткам без секунд.
    /// </summary>
    /// <param name="value">Время из привязки.</param>
    /// <returns>Время в пределах суток.</returns>
    private static TimeSpan ClampTimeOfDay(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value >= TimeSpan.FromDays(1)
            ? new TimeSpan(23, 59, 0)
            : new TimeSpan(value.Hours, value.Minutes, 0);
    }

    /// <summary>
    /// Объявление изменилось: баннер показывается только непустым.
    /// </summary>
    partial void OnAnnouncementChanged(string value) =>
        OnPropertyChanged(nameof(HasAnnouncement));

    /// <summary>
    /// Первое чтение закончилось: переключаются «содержимое» и «ошибка».
    /// </summary>
    partial void OnIsFirstLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
    }

    /// <summary>
    /// Причина чтения изменилась: переключаются «содержимое» и «ошибка».
    /// </summary>
    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
    }

    /// <summary>
    /// Запись закончилась: кнопки снова доступны.
    /// </summary>
    partial void OnIsSavingChanged(bool value) => NotifyDerivedState();

    /// <summary>
    /// Записей в диалоге конфликта больше, чем поместилось: строка «показаны не все» пересобирается.
    /// </summary>
    partial void OnHasMoreConflictsChanged(bool value) =>
        OnPropertyChanged(nameof(MoreConflictsText));

    /// <summary>
    /// Салон сменился: неделя и исключения пересобираются.
    /// </summary>
    partial void OnSelectedSalonChanged(BarberScheduleSalonItem? value)
    {
        if (!_isRebuilding)
        {
            RebuildSalon();
        }

        OnPropertyChanged(nameof(SingleSalonText));
    }

    /// <summary>
    /// Дата исключения изменилась: ошибка снимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewExceptionDateChanged(DateTime value)
    {
        var clamped = ClampDate(value, ExceptionMinDate, ExceptionMaxDate);

        if (clamped != value)
        {
            NewExceptionDate = clamped;

            return;
        }

        ExceptionError = null;

        AddExceptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Вид исключения изменился: ошибка снимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewExceptionIsDayOffChanged(bool value)
    {
        ExceptionError = null;

        AddExceptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Начало своих часов изменилось: время зажимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewExceptionStartTimeChanged(TimeSpan value)
    {
        var clamped = ClampTimeOfDay(value);

        if (clamped != value)
        {
            NewExceptionStartTime = clamped;

            return;
        }

        ExceptionError = null;

        AddExceptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Окончание своих часов изменилось: время зажимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewExceptionEndTimeChanged(TimeSpan value)
    {
        var clamped = ClampTimeOfDay(value);

        if (clamped != value)
        {
            NewExceptionEndTime = clamped;

            return;
        }

        ExceptionError = null;

        AddExceptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Пометка исключения изменилась: ошибка снимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewExceptionReasonChanged(string value)
    {
        ExceptionError = null;

        AddExceptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Начало отпуска изменилось: дата зажимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewTimeOffStartDateChanged(DateTime value)
    {
        var clamped = ClampDate(value, TimeOffMinDate, TimeOffMaxDate);

        if (clamped != value)
        {
            NewTimeOffStartDate = clamped;

            return;
        }

        TimeOffError = null;

        AddTimeOffCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Конец отпуска изменился: дата зажимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewTimeOffEndDateChanged(DateTime value)
    {
        var clamped = ClampDate(value, TimeOffMinDate, TimeOffMaxDate);

        if (clamped != value)
        {
            NewTimeOffEndDate = clamped;

            return;
        }

        TimeOffError = null;

        AddTimeOffCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Пометка отпуска изменилась: ошибка снимается, кнопка пересчитывается.
    /// </summary>
    partial void OnNewTimeOffReasonChanged(string value)
    {
        TimeOffError = null;

        AddTimeOffCommand.NotifyCanExecuteChanged();
    }
}
