using System.Globalization;
using System.Net;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.BarberRequests;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Стать мастером»: заявка клиента на роль мастера — форма, статус, ошибки чтения и отправки.
/// </summary>
/// <remarks>
/// <para>
/// Страница повторяет веб (<c>frontend/src/pages/become-a-barber-page.tsx</c>,
/// <c>barber-request-form.tsx</c>, <c>barber-request-status.tsx</c>): пока заявка читается —
/// индикатор, заявки нет — форма, заявка есть — карточка статуса. Это ровно пять состояний,
/// и они взаимоисключающие: у экрана нет «пустого» вида, поэтому переходы идут через один
/// приватный метод, а не через флаги в каждом обработчике.
/// </para>
/// <para>
/// Чтение лечится само: сбой сети, <c>5xx</c> и <c>429</c> повторяются с нарастающей паузой
/// (<see cref="AutoRetryLoop"/>), пока страница видна, а «Повторить» значит «повторить
/// сейчас». Запись не повторяется автоматически: после сбоя отправки исход неизвестен, и
/// тихий повтор мог бы создать вторую заявку — поэтому кнопка, а не цикл.
/// </para>
/// <para>
/// <c>201</c> сразу показывает карточку «Заявка отправлена»: ответ уже содержит заявку, а
/// второе чтение только гонялось бы с ещё не зафиксированным состоянием. <c>409</c> —
/// единственный отказ, который лечится чтением: заявка уже есть, и человеку нужен её
/// фактический статус, а не общая строка про неудачу.
/// </para>
/// <para>
/// Тексты взяты из веба дословно, кроме слова «барбер» → «мастер»: это термин интерфейса
/// (GLOSSARY.md, «Words on the screen»), а <c>Barber</c> остаётся именем роли в коде и в API.
/// Навигации здесь нет: Core не знает про MAUI и Shell, поэтому уход выражается событием
/// <see cref="LeaveRequested"/>.
/// </para>
/// </remarks>
public sealed partial class BarberRequestViewModel : ObservableObject, IDisposable
{
    private static string ReadFailedText => Loc.T("Не удалось проверить заявку.");
    private static string NetworkText => Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string TooManyRequestsText => Loc.T("Слишком много запросов. Подождите минуту и повторите.");
    private static string SubmitFailedText => Loc.T("Не удалось отправить заявку. Попробуйте ещё раз.");
    private static string OnlyCustomerText => Loc.T("Заявку может подать только клиент.");

    /// <summary>
    /// Заявка уже есть, а перечитать её не удалось: «Повторить» здесь бессмысленен, потому что
    /// повтор упрётся в тот же <c>409</c>.
    /// </summary>
    private static string AlreadyPendingText => Loc.T(
        "Заявка уже на рассмотрении. Дождитесь решения администратора — статус виден в кабинете.");

    private static string NameRequiredText => Loc.T("Введите имя, которое увидят клиенты.");
    private static string NameTooLongText => Loc.T("Имя — не длиннее 120 символов.");
    private static string ExperienceRequiredText => Loc.T("Укажите стаж в годах.");
    private static string ExperienceRangeText => Loc.T("Стаж — целое число от 1 года до 70 лет.");
    private static string CityRequiredText => Loc.T("Укажите город.");
    private static string CityTooLongText => Loc.T("Город — не длиннее 100 символов.");
    private static string AboutTooLongText => Loc.T("О себе — не длиннее 2000 символов.");
    private static string RejectionReasonMissingText => Loc.T("Причина не указана.");
    private static string SalonYesText => Loc.T("Да, уже в салоне");
    private static string SalonNoText => Loc.T("Нет, работает сам");

    private const string DisplayNameCode = "barberProfile.displayName";
    private const string DisplayNameTooLongCode = "barberProfile.displayName.tooLong";
    private const string ExperienceCode = "barberProfile.yearsOfExperience";
    private const string CityCode = "barberRequest.city";
    private const string CityTooLongCode = "barberRequest.city.tooLong";
    private const string AboutTooLongCode = "barberRequest.about.tooLong";

    /// <summary>
    /// Границы полей совпадают с доменом и с вебом: клиент не должен отправлять заведомо
    /// невалидное тело, а сервер остаётся источником истины.
    /// </summary>
    private const int MaxDisplayNameLength = 120;

    private const int MinExperienceYears = 1;
    private const int MaxExperienceYears = 70;
    private const int MaxCityLength = 100;
    private const int MaxAboutLength = 2000;

    private readonly IBarberRequestsApi _api;
    private readonly TokenSession _session;

    /// <summary>
    /// Часы: моменты подачи и рассмотрения показываются в поясе устройства, а правило «год
    /// только у момента не текущего года» проверяется без ожидания нового года.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Цикл автоповтора чтения заявки: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    private readonly AutoRetryLoop _autoRetry;

    /// <summary>
    /// Вид последнего сбоя чтения: по нему цикл решает, повторять ли чтение.
    /// </summary>
    /// <remarks>
    /// Запоминается для каждого неуспеха, а не только для повторяемого: иначе «транспорт, потом
    /// 403» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>, и «Повторить»
    /// снова запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _readFailureKind;

    /// <summary>
    /// Страница сейчас видна: цикл автоповтора работает только пока это так.
    /// </summary>
    private bool _isPageVisible;

    private bool _disposed;

    /// <summary>
    /// Создаёт экран заявки на роль мастера.
    /// </summary>
    /// <remarks>
    /// <paramref name="timeProvider"/> и <paramref name="autoRetry"/> необязательны и стоят
    /// последними: DI создаёт экран по первым двум зависимостям, а тест подставляет свои часы
    /// и своё расписание повторов, не собирая ничего лишнего.
    /// </remarks>
    /// <param name="api">Чтение своей заявки и её создание.</param>
    /// <param name="session">Сессия: по ней выполняется выход после одобрения заявки.</param>
    /// <param name="timeProvider">Часы; <c>null</c> — системные.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора чтения; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    public BarberRequestViewModel(
        IBarberRequestsApi api,
        TokenSession session,
        TimeProvider? timeProvider = null,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);

        _api = api;
        _session = session;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;
    }

    /// <summary>
    /// Экран просит закрыться: страница снимает себя со стека.
    /// </summary>
    /// <remarks>
    /// Событие, а не вызов навигации: Core не знает про Shell/MAUI, а «Выйти и войти снова» и
    /// «К входу» — это один и тот же уход с экрана, разное только действие перед ним.
    /// </remarks>
    public event EventHandler? LeaveRequested;

    /// <summary>
    /// Показывать индикатор: заявка читается.
    /// </summary>
    [ObservableProperty]
    private bool _showLoading = true;

    /// <summary>
    /// Показывать форму: заявки ещё нет.
    /// </summary>
    [ObservableProperty]
    private bool _showForm;

    /// <summary>
    /// Показывать карточку статуса: заявка есть или только что создана.
    /// </summary>
    [ObservableProperty]
    private bool _showStatus;

    /// <summary>
    /// Показывать ошибку чтения с причиной и «Повторить».
    /// </summary>
    [ObservableProperty]
    private bool _showError;

    /// <summary>
    /// Показывать состояние истёкшей сессии: пара мертва, и читать больше нечем.
    /// </summary>
    [ObservableProperty]
    private bool _showSessionExpired;

    /// <summary>
    /// Причина ошибки чтения; пусто, когда ошибки нет.
    /// </summary>
    /// <remarks>
    /// Заголовок ошибки («Не удалось проверить заявку») живёт в XAML, как и на других экранах:
    /// причина — данные, заголовок — вёрстка. Английский <c>detail</c> сюда не попадает никогда.
    /// </remarks>
    [ObservableProperty]
    private string _readError = string.Empty;

    /// <summary>
    /// Цикл сам повторяет чтение: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Состояние цикла, а не экрана: свойство переключается подпиской на
    /// <see cref="AutoRetryLoop.StateChanged"/> и ничего не решает само.
    /// </remarks>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Заявка из последнего ответа сервера; <c>null</c> — заявки нет.
    /// </summary>
    [ObservableProperty]
    private BarberRequestResponse? _request;

    /// <summary>
    /// Заголовок карточки статуса: по статусу заявки либо «Заявка отправлена» после <c>201</c>.
    /// </summary>
    [ObservableProperty]
    private string _statusHeading = string.Empty;

    /// <summary>
    /// Чип статуса: «На рассмотрении», «Одобрена» или «Отклонена».
    /// </summary>
    [ObservableProperty]
    private string _statusLabel = string.Empty;

    /// <summary>
    /// Заявка одобрена: карточка показывает действие выхода и повторного входа.
    /// </summary>
    [ObservableProperty]
    private bool _isApproved;

    /// <summary>
    /// Заявка отклонена: карточка показывает причину и «Подать заявку снова».
    /// </summary>
    [ObservableProperty]
    private bool _canReapply;

    /// <summary>
    /// Заявка только что создана этим экраном: заголовок «Заявка отправлена».
    /// </summary>
    /// <remarks>
    /// Отдельный флаг, а не признак статуса: после <c>201</c> заголовок говорит о событии
    /// («отправлена»), а не о состоянии, и второго чтения для этого не нужно.
    /// </remarks>
    [ObservableProperty]
    private bool _isJustCreated;

    /// <summary>
    /// Имя из заявки.
    /// </summary>
    [ObservableProperty]
    private string _displayNameText = string.Empty;

    /// <summary>
    /// Стаж из заявки: «5 лет», «1 год».
    /// </summary>
    [ObservableProperty]
    private string _experienceText = string.Empty;

    /// <summary>
    /// Город из заявки.
    /// </summary>
    [ObservableProperty]
    private string _cityText = string.Empty;

    /// <summary>
    /// Работает ли уже в салоне: «Да, уже в салоне» или «Нет, работает сам».
    /// </summary>
    [ObservableProperty]
    private string _salonText = string.Empty;

    /// <summary>
    /// Когда заявка подана, в поясе устройства.
    /// </summary>
    [ObservableProperty]
    private string _submittedAtText = string.Empty;

    /// <summary>
    /// Когда заявка рассмотрена, в поясе устройства; пусто, пока решения нет.
    /// </summary>
    /// <remarks>
    /// Пустая строка, а не «Ещё не было»: строка «Рассмотрена» у заявки без решения просто не
    /// показывается — так же поступает веб.
    /// </remarks>
    [ObservableProperty]
    private string _reviewedAtText = string.Empty;

    /// <summary>
    /// Рассказ о себе; пусто, если его не заполняли.
    /// </summary>
    [ObservableProperty]
    private string _aboutText = string.Empty;

    /// <summary>
    /// Причина отказа; для заявки без причины — «Причина не указана.».
    /// </summary>
    [ObservableProperty]
    private string _rejectionReasonText = string.Empty;

    /// <summary>
    /// Имя из формы: под ним человек хочет работать.
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Стаж из формы; строка, а не число — так <c>Entry</c> не теряет ввод и показывает
    /// ошибку вместо того, чтобы молча округлить значение.
    /// </summary>
    [ObservableProperty]
    private string _experienceYears = string.Empty;

    /// <summary>
    /// Город из формы.
    /// </summary>
    [ObservableProperty]
    private string _city = string.Empty;

    /// <summary>
    /// Рассказ о себе из формы; пустой уходит на сервер как <c>null</c>, а не как «».
    /// </summary>
    [ObservableProperty]
    private string _about = string.Empty;

    /// <summary>
    /// Уже работает в салоне.
    /// </summary>
    [ObservableProperty]
    private bool _hasSalon;

    /// <summary>
    /// Ошибка поля «Имя».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisplayNameError))]
    private string? _displayNameError;

    /// <summary>
    /// Ошибка поля «Стаж».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExperienceYearsError))]
    private string? _experienceYearsError;

    /// <summary>
    /// Ошибка поля «Город».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCityError))]
    private string? _cityError;

    /// <summary>
    /// Ошибка поля «О себе».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAboutError))]
    private string? _aboutError;

    /// <summary>
    /// Общая ошибка отправки: отказ, который не относится к известному полю.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubmitError))]
    private string? _submitError;

    /// <summary>
    /// Идёт отправка: кнопка называется «Отправляем…», повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitButtonText))]
    private bool _isSubmitting;

    /// <summary>
    /// Показывать ли «Повторить» при ошибке отправки.
    /// </summary>
    /// <remarks>
    /// Повтор предлагается только там, где он может помочь: сеть, таймаут, <c>429</c>, <c>5xx</c>
    /// и неизвестный <c>400</c>. Отказ по роли, <c>409</c> и ошибки полей повторять бессмысленно.
    /// </remarks>
    [ObservableProperty]
    private bool _canRetrySubmit;

    /// <summary>
    /// Есть ошибка поля «Имя».
    /// </summary>
    public bool HasDisplayNameError => !string.IsNullOrEmpty(DisplayNameError);

    /// <summary>
    /// Есть ошибка поля «Стаж».
    /// </summary>
    public bool HasExperienceYearsError => !string.IsNullOrEmpty(ExperienceYearsError);

    /// <summary>
    /// Есть ошибка поля «Город».
    /// </summary>
    public bool HasCityError => !string.IsNullOrEmpty(CityError);

    /// <summary>
    /// Есть ошибка поля «О себе».
    /// </summary>
    public bool HasAboutError => !string.IsNullOrEmpty(AboutError);

    /// <summary>
    /// Есть общая ошибка отправки.
    /// </summary>
    public bool HasSubmitError => !string.IsNullOrEmpty(SubmitError);

    /// <summary>
    /// Надпись на кнопке отправки.
    /// </summary>
    public string SubmitButtonText => IsSubmitting
        ? Loc.T("Отправляем…")
        : Loc.T("Отправить заявку");

    /// <summary>
    /// Завершается, когда цикл автоповтора останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора: тестам нужен счётчик попыток, приложению — нет.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Читает свою заявку при появлении страницы.
    /// </summary>
    /// <remarks>
    /// <c>request: null</c> — штатное состояние формы, а не ошибка. <c>401</c> показывает
    /// истёкшую сессию: обработчик уже пробовал продлить пару, и второй раз читать нечем.
    /// Цикл прошлого появления останавливается: свежее чтение не должно гоняться с ним.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        StopAutoRetryLoop();

        await LoadAsync(silent: false, cancellationToken);
    }

    /// <summary>
    /// Сообщает, что страница видна, и запускает автоповтор неудачного чтения заявки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зовётся страницей при каждом появлении и каждом возврате из фона. Идемпотентен: без
    /// ошибки, при окончательном отказе или уже работающем цикле ничего не делает.
    /// </para>
    /// <para>
    /// Запуск и остановка обязаны идти из UI-потока: <see cref="AutoRetryLoop.StateChanged"/>
    /// синхронно меняет привязанный <see cref="IsAutoRetrying"/>. Поэтому метод зовут только
    /// из жизненного цикла страницы, а не из фонового <c>catch</c>.
    /// </para>
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Сообщает, что страница скрыта, и останавливает автоповтор чтения заявки.
    /// </summary>
    /// <remarks>
    /// Идемпотентен. Остановка отменяет и паузу, и запрос в полёте, а <see cref="IsAutoRetrying"/>
    /// сразу становится <c>false</c>: скрытая страница не показывает работающий повтор и не
    /// ходит в сеть.
    /// </remarks>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
    }

    /// <summary>
    /// Освобождает экран: останавливает цикл и отписывается от него.
    /// </summary>
    /// <remarks>
    /// Страница уходит из стека, и тихая попытка не должна пережить свой ViewModel. Цикл
    /// останавливается до отписки, чтобы <see cref="IsAutoRetrying"/> успел стать <c>false</c>
    /// и привязка не осталась в положении «повтор идёт».
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isPageVisible = false;

        _autoRetry.Stop();
        _autoRetry.StateChanged -= OnAutoRetryStateChanged;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Отправляет заявку на роль мастера.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Сначала клиентские проверки — дословно вебовые: пустое имя, стаж вне 1..70, пустой
    /// город и слишком длинный рассказ отсекаются до запроса. Если ошибка есть хотя бы в
    /// одном поле, запроса нет.
    /// </para>
    /// <para>
    /// Запись не повторяется сама: после сбоя исход неизвестен, поэтому решение остаётся за
    /// человеком, а повторное нажатие во время запроса игнорируется. Заявку от второй подачи
    /// защищает сервер (<c>409</c> и уникальный индекс), но лишний запрос всё равно не нужен.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        var displayName = DisplayName.Trim();
        var experienceText = ExperienceYears.Trim();
        var city = City.Trim();
        var about = About.Trim();
        var experienceYears = 0;

        DisplayNameError = null;
        ExperienceYearsError = null;
        CityError = null;
        AboutError = null;
        SubmitError = null;
        CanRetrySubmit = false;

        if (displayName.Length == 0)
        {
            DisplayNameError = NameRequiredText;
        }
        else if (displayName.Length > MaxDisplayNameLength)
        {
            DisplayNameError = NameTooLongText;
        }

        if (experienceText.Length == 0)
        {
            ExperienceYearsError = ExperienceRequiredText;
        }
        else if (!TryReadExperience(experienceText, out experienceYears))
        {
            ExperienceYearsError = ExperienceRangeText;
        }

        if (city.Length == 0)
        {
            CityError = CityRequiredText;
        }
        else if (city.Length > MaxCityLength)
        {
            CityError = CityTooLongText;
        }

        if (about.Length > MaxAboutLength)
        {
            AboutError = AboutTooLongText;
        }

        if (HasDisplayNameError || HasExperienceYearsError || HasCityError || HasAboutError)
        {
            return;
        }

        IsSubmitting = true;

        try
        {
            var request = await _api.CreateAsync(
                new CreateBarberRequestRequest(
                    displayName,
                    experienceYears,
                    city,
                    about.Length == 0 ? null : about,
                    HasSalon),
                CancellationToken.None);

            ApplyRequest(request, justCreated: true);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Пара умерла: заявка не отправлена, и продолжать на этом экране нечем.
            ApplyState(PageState.SessionExpired);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            await ResolveAlreadyPendingAsync();
        }
        catch (BarberSearchApiException exception)
        {
            ApplySubmitFailure(exception);
        }
        catch (HttpRequestException)
        {
            SubmitError = NetworkText;
            CanRetrySubmit = true;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            SubmitError = NetworkText;
            CanRetrySubmit = true;
        }
        catch (InvalidOperationException)
        {
            // Пустой ответ сервера: заявка могла создаться, поэтому повтор разрешён — вторую
            // всё равно не пустит <c>409</c>.
            SubmitError = SubmitFailedText;
            CanRetrySubmit = true;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Повторяет чтение заявки после ошибки — сразу, по кнопке.
    /// </summary>
    /// <remarks>
    /// «Повторить» значит «повторить сейчас»: цикл останавливается, заявка читается без паузы,
    /// а цикл запускается заново только при новом повторяемом сбое. Публичный
    /// <see cref="StopAutoRetry"/> здесь не годится — он ещё и пометил бы страницу скрытой.
    /// </remarks>
    [RelayCommand]
    private async Task RetryReadAsync()
    {
        StopAutoRetryLoop();

        await LoadAsync(silent: false, CancellationToken.None);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Открывает пустую форму после отказа — «Подать заявку снова».
    /// </summary>
    /// <remarks>
    /// Веб не предзаполняет повторную заявку, и здесь тоже: поля очищаются вместе с ошибками,
    /// чтобы человек не отправил прошлый текст по инерции. Причина отказа при этом остаётся в
    /// памяти — её человек уже прочитал на карточке.
    /// </remarks>
    [RelayCommand]
    private void Reapply()
    {
        DisplayName = string.Empty;
        ExperienceYears = string.Empty;
        City = string.Empty;
        About = string.Empty;
        HasSalon = false;

        DisplayNameError = null;
        ExperienceYearsError = null;
        CityError = null;
        AboutError = null;
        SubmitError = null;
        CanRetrySubmit = false;

        ApplyState(PageState.Form);
    }

    /// <summary>
    /// Уход с экрана без действия: поднимает <see cref="LeaveRequested"/>.
    /// </summary>
    /// <remarks>
    /// Кнопка «К входу» в состоянии истёкшей сессии: хаб к этому моменту уже показывает форму
    /// входа, потому что пару погасил обработчик, и странице остаётся только закрыться.
    /// </remarks>
    [RelayCommand]
    private void Leave() => LeaveRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Выходит из аккаунта и закрывает экран — действие после одобрения заявки.
    /// </summary>
    /// <remarks>
    /// После одобрения пара уже мертва: смена роли ротирует <c>SecurityStamp</c>. Поэтому выход
    /// честен и в случае живого токена, и в случае мёртвого, а новый вход приносит новую роль.
    /// Сбой очистки хранилища наружу не выходит: память сессии уже стёрта, и человеку нужен
    /// экран входа, а не сообщение об ошибке вместо выхода.
    /// </remarks>
    [RelayCommand]
    private async Task SignOutAndLeaveAsync()
    {
        StopAutoRetry();

        try
        {
            await _session.ClearAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Устройство без рабочего хранилища: сессии в памяти уже нет, повторять нечего.
        }

        LeaveRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Читает свою заявку и раскладывает ответ по экрану.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Тихая попытка цикла не показывает индикатор и не стирает причину ошибки до ответа:
    /// иначе на каждой паузе экран мигал бы пустым местом и кнопкой, которую некуда нажать.
    /// </para>
    /// <para>
    /// <c>401</c> — единственный ответ, который уводит в состояние истёкшей сессии: пару уже
    /// пытались продлить, и следующий запрос повторит отказ. Вид сбоя запоминается для каждого
    /// неуспеха, чтобы «Повторить» не запускала цикл на окончательном отказе.
    /// </para>
    /// </remarks>
    /// <param name="silent">Тихая попытка цикла: не показывать индикатор.</param>
    /// <param name="cancellationToken">Токен отмены страницы или цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadAsync(bool silent, CancellationToken cancellationToken)
    {
        if (!silent)
        {
            ApplyState(PageState.Loading);
        }

        var kind = ApiFailureKind.None;

        try
        {
            var response = await _api.GetMineAsync(cancellationToken);

            ReadError = string.Empty;
            _readFailureKind = ApiFailureKind.None;

            ApplyRequest(response.Request, justCreated: false);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            kind = ApiFailureKind.Terminal;

            // Причина у состояния истёкшей сессии своя, поэтому ReadError остаётся пустым.
            ReadError = string.Empty;
            _readFailureKind = kind;

            ApplyState(PageState.SessionExpired);
        }
        catch (BarberSearchApiException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            ReadError = exception.StatusCode == HttpStatusCode.TooManyRequests
                ? TooManyRequestsText
                : ReadFailedText;
            _readFailureKind = kind;

            ApplyState(PageState.Error);
        }
        catch (HttpRequestException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            ReadError = NetworkText;
            _readFailureKind = kind;

            ApplyState(PageState.Error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            kind = ApiFailureKind.Transport;

            ReadError = NetworkText;
            _readFailureKind = kind;

            ApplyState(PageState.Error);
        }
        catch (OperationCanceledException)
        {
            // Настоящая отмена: страницу закрыли, ошибку показывать некому.
            return ApiFailureKind.None;
        }
        catch (InvalidOperationException)
        {
            // Пустое тело ответа: для экрана это та же недоступность заявки.
            kind = ApiFailureKind.Terminal;

            ReadError = ReadFailedText;
            _readFailureKind = kind;

            ApplyState(PageState.Error);
        }

        if (kind != ApiFailureKind.None)
        {
            TryStartAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла: перечитать заявку, не гася причину ошибки.
    /// </summary>
    /// <remarks>
    /// Условия перепроверяются на момент попытки: пока цикл ждал паузу, ошибку могли снять или
    /// сессию погасить. Тогда повторять нечего, и цикл останавливается.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private Task<ApiFailureKind> RetryReadAttemptAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ReadError) || !_readFailureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadAsync(silent: true, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл, если страница видна, а ошибка чтения повторяемая.
    /// </summary>
    /// <remarks>
    /// «Видна» — сигнал страницы, а не следствие нажатия: повтор запускается и из завершения
    /// чтения, но только пока страница видна. Второй цикл поверх первого удвоил бы запросы,
    /// поэтому <see cref="AutoRetryLoop.Start"/> при работающем цикле ничего не делает.
    /// </remarks>
    private void TryStartAutoRetry()
    {
        if (_disposed || !_isPageVisible || ShowLoading)
        {
            return;
        }

        if (string.IsNullOrEmpty(ReadError) || !_readFailureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetryReadAttemptAsync);
    }

    /// <summary>
    /// Останавливает цикл, не трогая признак «страница видна».
    /// </summary>
    /// <remarks>
    /// Нужен кнопке «Повторить» и свежему чтению: они останавливают фон, но страница остаётся
    /// видимой, и после ответа цикл имеет право запуститься снова. Публичный
    /// <see cref="StopAutoRetry"/> дополнительно помечает страницу скрытой — для них это неверно.
    /// </remarks>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Показывает ровно одно состояние страницы.
    /// </summary>
    /// <remarks>
    /// Единственный переход состояния: пять флагов привязки включаются и выключаются вместе,
    /// поэтому «два состояния сразу» или «ни одного» не могут появиться по забывчивости.
    /// </remarks>
    /// <param name="state">Состояние, которое должно остаться единственным видимым.</param>
    private void ApplyState(PageState state)
    {
        ShowLoading = state == PageState.Loading;
        ShowForm = state == PageState.Form;
        ShowStatus = state == PageState.Status;
        ShowError = state == PageState.Error;
        ShowSessionExpired = state == PageState.SessionExpired;
    }

    /// <summary>
    /// Раскладывает заявку по карточке статуса или открывает форму, если заявки нет.
    /// </summary>
    /// <remarks>
    /// Тексты деталей собираются здесь, а не в XAML: форматирование дат и стажа живёт в
    /// <see cref="AccountText"/> и <see cref="BarberText"/>, то есть там же, где у остальных
    /// экранов. Для только что созданной заявки заголовок говорит о событии, а не о статусе.
    /// </remarks>
    /// <param name="request">Заявка из ответа; <c>null</c> — заявки нет.</param>
    /// <param name="justCreated">Заявка создана этим экраном (<c>201</c>), а не прочитана.</param>
    private void ApplyRequest(BarberRequestResponse? request, bool justCreated)
    {
        Request = request;

        if (request is null)
        {
            ClearStatus();
            ApplyState(PageState.Form);

            return;
        }

        var now = _timeProvider.GetUtcNow();

        IsJustCreated = justCreated;
        StatusHeading = justCreated
            ? Loc.T("Заявка отправлена")
            : HeadingFor(request.Status);
        StatusLabel = LabelFor(request.Status);
        IsApproved = request.Status == BarberRequestStatus.Approved;
        CanReapply = request.Status == BarberRequestStatus.Rejected;

        DisplayNameText = request.DisplayName;
        ExperienceText = BarberText.Experience(request.YearsOfExperience);
        CityText = request.City;
        SalonText = request.HasSalon ? SalonYesText : SalonNoText;
        SubmittedAtText = AccountText.Moment(request.CreatedAt, TimeZoneInfo.Local, now);
        ReviewedAtText = request.ReviewedAt is { } reviewedAt
            ? AccountText.Moment(reviewedAt, TimeZoneInfo.Local, now)
            : string.Empty;
        AboutText = request.About ?? string.Empty;
        RejectionReasonText = request.RejectionReason ?? RejectionReasonMissingText;

        ApplyState(PageState.Status);
    }

    /// <summary>
    /// Гасит карточку статуса: заявки нет, показывать нечего.
    /// </summary>
    private void ClearStatus()
    {
        StatusHeading = string.Empty;
        StatusLabel = string.Empty;
        IsApproved = false;
        CanReapply = false;
        IsJustCreated = false;

        DisplayNameText = string.Empty;
        ExperienceText = string.Empty;
        CityText = string.Empty;
        SalonText = string.Empty;
        SubmittedAtText = string.Empty;
        ReviewedAtText = string.Empty;
        AboutText = string.Empty;
        RejectionReasonText = string.Empty;
    }

    /// <summary>
    /// Разрешает <c>409</c>: перечитывает заявку и показывает её фактический статус.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Вторая подача (например, с двух устройств) — не ошибка ввода, и общая строка «не
    /// удалось отправить» здесь врала бы: заявка есть, у неё есть решение или ожидание.
    /// </para>
    /// <para>
    /// Если перечитать не удалось, показан текст про активную заявку и «Повторить» не
    /// предлагается: повтор упёрся бы в тот же <c>409</c>. Цикл чтения при этом не взводится —
    /// экран формы не должен начинать фоновые чтения из-за записи.
    /// </para>
    /// </remarks>
    private async Task ResolveAlreadyPendingAsync()
    {
        try
        {
            var response = await _api.GetMineAsync(CancellationToken.None);

            if (response.Request is null)
            {
                // Сервер сказал «заявка есть», а чтение её не отдало: верим отказу и не
                // открываем форму, иначе следующий <c>POST</c> снова получит <c>409</c>.
                SubmitError = AlreadyPendingText;
                CanRetrySubmit = false;

                return;
            }

            ReadError = string.Empty;
            _readFailureKind = ApiFailureKind.None;

            ApplyRequest(response.Request, justCreated: false);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            ApplyState(PageState.SessionExpired);
        }
        catch (Exception)
        {
            // Причина не важна: заявка уже есть, и сказать надо именно это.
            SubmitError = AlreadyPendingText;
            CanRetrySubmit = false;
        }
    }

    /// <summary>
    /// Раскладывает отказ отправки по текстам.
    /// </summary>
    /// <remarks>
    /// <c>403</c> и <c>409</c> окончательны, <c>429</c> просит сбавить темп, остальное —
    /// общая строка с повтором. Английский <c>detail</c> и <c>ValidationErrors</c> не
    /// показываются никогда: они диагностические, а человеку нужна причина на своём языке.
    /// Известные коды <c>400</c> уходят под поля и общей строки не добавляют.
    /// </remarks>
    /// <param name="exception">Отказ сервера.</param>
    private void ApplySubmitFailure(BarberSearchApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            SubmitError = OnlyCustomerText;
            CanRetrySubmit = false;

            return;
        }

        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            SubmitError = TooManyRequestsText;
            CanRetrySubmit = true;

            return;
        }

        if (exception.StatusCode == HttpStatusCode.BadRequest && ApplyFieldErrors(exception))
        {
            return;
        }

        SubmitError = SubmitFailedText;
        CanRetrySubmit = true;
    }

    /// <summary>
    /// Раскладывает <c>400</c> по полям формы.
    /// </summary>
    /// <remarks>
    /// Сервер валидирует все поля сразу и присылает несколько кодов за один ответ, поэтому
    /// раскладываются все совпавшие: человек исправляет причину, а не проходит по одной
    /// ошибке за нажатие. Текст берётся из веба, а не из английского <c>ValidationErrors</c>.
    /// </remarks>
    /// <param name="exception">Отказ валидации.</param>
    /// <returns><c>true</c>, если хотя бы один код относится к известному полю.</returns>
    private bool ApplyFieldErrors(BarberSearchApiException exception)
    {
        var hasFieldError = false;

        if (HasCode(exception, DisplayNameCode))
        {
            DisplayNameError = NameRequiredText;
            hasFieldError = true;
        }

        if (HasCode(exception, DisplayNameTooLongCode))
        {
            DisplayNameError = NameTooLongText;
            hasFieldError = true;
        }

        if (HasCode(exception, ExperienceCode))
        {
            ExperienceYearsError = ExperienceRangeText;
            hasFieldError = true;
        }

        if (HasCode(exception, CityCode))
        {
            CityError = CityRequiredText;
            hasFieldError = true;
        }

        if (HasCode(exception, CityTooLongCode))
        {
            CityError = CityTooLongText;
            hasFieldError = true;
        }

        if (HasCode(exception, AboutTooLongCode))
        {
            AboutError = AboutTooLongText;
            hasFieldError = true;
        }

        return hasFieldError;
    }

    /// <summary>
    /// Читает стаж из строки так же снисходительно, как веб.
    /// </summary>
    /// <remarks>
    /// Веб проверяет <c>Number.isInteger(Number(value))</c>, поэтому «5.0» для него — целое
    /// число лет. <see cref="int.TryParse(string, out int)"/> такое отвергал бы, и человек,
    /// вставивший «5.0», получил бы ошибку на верном значении. Дробный стаж по-прежнему
    /// отклоняется: округлять его молча нельзя, это чужие годы в профиле.
    /// </remarks>
    /// <param name="value">Строка из поля «Опыт, лет» без пробелов по краям.</param>
    /// <param name="years">Разобранное число лет при успехе.</param>
    /// <returns><c>true</c>, если значение — целое число в допустимых границах.</returns>
    private static bool TryReadExperience(string value, out int years)
    {
        years = 0;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || parsed < MinExperienceYears
            || parsed > MaxExperienceYears
            || parsed != Math.Floor(parsed))
        {
            return false;
        }

        years = (int)parsed;

        return true;
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    /// <param name="exception">Отказ сервера.</param>
    /// <param name="code">Публичный код.</param>
    /// <returns><c>true</c>, если код есть в ответе.</returns>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Заголовок карточки по статусу заявки.
    /// </summary>
    /// <param name="status">Статус из ответа.</param>
    /// <returns>Русский заголовок; неизвестный статус читается как «на рассмотрении».</returns>
    private static string HeadingFor(BarberRequestStatus status) => status switch
    {
        BarberRequestStatus.Approved => Loc.T("Заявка одобрена"),
        BarberRequestStatus.Rejected => Loc.T("Заявка отклонена"),
        _ => Loc.T("Заявка на рассмотрении"),
    };

    /// <summary>
    /// Чип статуса по статусу заявки.
    /// </summary>
    /// <param name="status">Статус из ответа.</param>
    /// <returns>Русская подпись чипа.</returns>
    private static string LabelFor(BarberRequestStatus status) => status switch
    {
        BarberRequestStatus.Approved => Loc.T("Одобрена"),
        BarberRequestStatus.Rejected => Loc.T("Отклонена"),
        _ => Loc.T("На рассмотрении"),
    };

    /// <summary>
    /// Правка имени снимает ошибку под полем и общую ошибку отправки.
    /// </summary>
    /// <remarks>
    /// Прошлый отказ относился к прошлому телу запроса: после правки он только путает.
    /// </remarks>
    /// <param name="value">Новое значение поля.</param>
    partial void OnDisplayNameChanged(string value)
    {
        _ = value;

        DisplayNameError = null;
        OnFormInputChanged();
    }

    /// <summary>
    /// Правка стажа снимает ошибку под полем и общую ошибку отправки.
    /// </summary>
    /// <param name="value">Новое значение поля.</param>
    partial void OnExperienceYearsChanged(string value)
    {
        _ = value;

        ExperienceYearsError = null;
        OnFormInputChanged();
    }

    /// <summary>
    /// Правка города снимает ошибку под полем и общую ошибку отправки.
    /// </summary>
    /// <param name="value">Новое значение поля.</param>
    partial void OnCityChanged(string value)
    {
        _ = value;

        CityError = null;
        OnFormInputChanged();
    }

    /// <summary>
    /// Правка рассказа о себе снимает ошибку под полем и общую ошибку отправки.
    /// </summary>
    /// <param name="value">Новое значение поля.</param>
    partial void OnAboutChanged(string value)
    {
        _ = value;

        AboutError = null;
        OnFormInputChanged();
    }

    /// <summary>
    /// Переключение «уже работаю в салоне» снимает общую ошибку отправки.
    /// </summary>
    /// <param name="value">Новое значение переключателя.</param>
    partial void OnHasSalonChanged(bool value)
    {
        _ = value;

        OnFormInputChanged();
    }

    /// <summary>
    /// Общая реакция формы на правку: прошлая ошибка отправки и её повтор больше не актуальны.
    /// </summary>
    private void OnFormInputChanged()
    {
        SubmitError = null;
        CanRetrySubmit = false;
    }

    /// <summary>
    /// Взаимоисключающие состояния экрана.
    /// </summary>
    /// <remarks>
    /// Один список вместо пяти независимых флагов: страница либо ждёт, либо показывает форму,
    /// либо статус, либо ошибку, либо истёкшую сессию — «пустого» вида у неё нет.
    /// </remarks>
    private enum PageState
    {
        /// <summary>Заявка читается: виден индикатор.</summary>
        Loading,

        /// <summary>Заявки нет: видна форма.</summary>
        Form,

        /// <summary>Заявка есть: видна карточка статуса.</summary>
        Status,

        /// <summary>Чтение не удалось: причина и «Повторить».</summary>
        Error,

        /// <summary>Сессия истекла: вход и «К входу».</summary>
        SessionExpired,
    }
}
