using System.Net;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Карточка заявки «Стать мастером»: анкета заявителя и решение администратора.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет веб (<c>admin/barber-request-detail.tsx</c>): сначала запись целиком — кто подал,
/// контакты, опыт, «о себе», статус и даты, — и только потом решение. Действия доступны
/// только у заявки в статусе <c>Pending</c>: повторное решение сервер отвергнет <c>409</c>,
/// поэтому кнопок у разобранной заявки нет.
/// </para>
/// <para>
/// Отказ требует причину: она не редактируется после решения и её видит заявитель, поэтому
/// подтверждение остаётся выключенным, пока причина пустая или длиннее
/// <see cref="MaxRejectionReasonLength"/> символов. Это то же правило, что у домена, — предел
/// берётся из <see cref="Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH"/>, а не
/// записывается на клиенте второй раз.
/// </para>
/// <para>
/// ViewModel не ходит по маршрутам: успешное решение возвращает <c>null</c>, и страница сама
/// уходит назад, где очередь перечитывается. Ошибка возвращается текстом и остаётся на экране:
/// человек должен видеть, что заявка не изменилась.
/// </para>
/// </remarks>
public sealed partial class AdminRequestViewModel : ObservableObject
{
    /// <summary>
    /// Наибольшая длина причины отказа — тот же предел, что проверяет домен.
    /// </summary>
    public const int MaxRejectionReasonLength =
        Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH;

    private const string InvalidStatusCode = "barberRequest.review.invalidStatus";
    private const string InvalidApplicantCode = "barberRequest.review.invalidApplicant";
    private const string NotFoundCode = "barberRequest.notFound";
    private const string ReasonRequiredCode = "barberRequest.rejection.reasonRequired";
    private const string ReasonTooLongCode = "barberRequest.rejection.reasonTooLong";

    private static string LoadErrorText => Loc.T("Не удалось загрузить заявку.");
    private static string ApproveFailedText => Loc.T("Не удалось одобрить заявку. Попробуйте ещё раз.");
    private static string RejectFailedText => Loc.T("Не удалось отклонить заявку. Попробуйте ещё раз.");

    private readonly IAdminRequestsApi _api;
    private readonly TokenSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _autoRetry;

    private Guid _requestId;
    private bool _hasRequestId;
    private bool _hasLoaded;
    private int _generation;
    private bool _isPageVisible;

    /// <summary>
    /// Вид последнего сбоя чтения: по нему решает цикл автоповтора.
    /// </summary>
    private ApiFailureKind _readFailureKind;

    /// <summary>
    /// Создаёт карточку заявки.
    /// </summary>
    /// <param name="api">Чтение заявки и два решения по ней.</param>
    /// <param name="session">Сессия: <c>401</c> гасит пару и показывает состояние входа.</param>
    /// <param name="timeProvider">Часы; <c>null</c> — системные.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора чтения; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    public AdminRequestViewModel(
        IAdminRequestsApi api,
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

        RejectionReasonHint = Loc.T(
            "До {0} символов. Причина не редактируется после отказа.",
            MaxRejectionReasonLength);
    }

    /// <summary>
    /// Идёт первая загрузка: индикатор вместо карточки.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDecision))]
    [NotifyPropertyChangedFor(nameof(ShowDecisionHint))]
    [NotifyPropertyChangedFor(nameof(ShowReasonPanel))]
    private bool _showLoading = true;

    /// <summary>
    /// Карточка загружена и показывается.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDecision))]
    [NotifyPropertyChangedFor(nameof(ShowDecisionHint))]
    [NotifyPropertyChangedFor(nameof(ShowReasonPanel))]
    [NotifyPropertyChangedFor(nameof(ShowReviewedNote))]
    [NotifyPropertyChangedFor(nameof(ShowApproveActionError))]
    private bool _showContent;

    /// <summary>
    /// Чтение не удалось: причина и «Повторить».
    /// </summary>
    [ObservableProperty]
    private bool _showError;

    /// <summary>
    /// Заявки нет: возможно, ссылка устарела.
    /// </summary>
    [ObservableProperty]
    private bool _showNotFound;

    /// <summary>
    /// Сервер ответил <c>403</c>: роль не администратор, данные не показываются.
    /// </summary>
    [ObservableProperty]
    private bool _showForbidden;

    /// <summary>
    /// Сервер ответил <c>401</c>: пары токенов больше нет, нужен вход.
    /// </summary>
    [ObservableProperty]
    private bool _showSignInRequired;

    /// <summary>
    /// Причина ошибки чтения; пусто, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string _readError = string.Empty;

    /// <summary>
    /// Цикл сам повторяет чтение: под причиной видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Имя, под которым заявитель хочет работать.
    /// </summary>
    [ObservableProperty]
    private string _displayNameText = string.Empty;

    /// <summary>
    /// Имя аккаунта заявителя.
    /// </summary>
    [ObservableProperty]
    private string _applicantNameText = string.Empty;

    /// <summary>
    /// Email заявителя: в админском контракте он есть, публичная страница его не показывает.
    /// </summary>
    [ObservableProperty]
    private string _applicantEmailText = string.Empty;

    /// <summary>
    /// Город из заявки.
    /// </summary>
    [ObservableProperty]
    private string _cityText = string.Empty;

    /// <summary>
    /// Стаж словами: «5 лет».
    /// </summary>
    [ObservableProperty]
    private string _experienceText = string.Empty;

    /// <summary>
    /// Работает ли заявитель в салоне: «Да» или «Нет».
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReviewedAt))]
    private string _reviewedAtText = string.Empty;

    /// <summary>
    /// Рассказ о себе; пусто, если его не заполняли.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAbout))]
    [NotifyPropertyChangedFor(nameof(HasNoAbout))]
    private string _aboutText = string.Empty;

    /// <summary>
    /// Причина отказа; для заявки без причины — «Причина не указана.».
    /// </summary>
    [ObservableProperty]
    private string _rejectionReasonText = string.Empty;

    /// <summary>
    /// Подпись статуса: «На рассмотрении», «Одобрена» или «Отклонена».
    /// </summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Заявка ждёт решения: у неё есть действия.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDecision))]
    [NotifyPropertyChangedFor(nameof(ShowDecisionHint))]
    [NotifyPropertyChangedFor(nameof(ShowReasonPanel))]
    [NotifyPropertyChangedFor(nameof(ShowReviewedNote))]
    [NotifyPropertyChangedFor(nameof(ShowApproveActionError))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanRunPrimaryAction))]
    private bool _isPending;

    /// <summary>
    /// Заявка одобрена: статус читается тоном «успех».
    /// </summary>
    [ObservableProperty]
    private bool _isApproved;

    /// <summary>
    /// Заявка отклонена: видна причина отказа.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRejectionReason))]
    private bool _isRejected;

    /// <summary>
    /// Открыта панель причины: решение принимается вторым шагом.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDecisionHint))]
    [NotifyPropertyChangedFor(nameof(ShowReasonPanel))]
    [NotifyPropertyChangedFor(nameof(ShowApproveActionError))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanRunPrimaryAction))]
    [NotifyPropertyChangedFor(nameof(CanConfirmReject))]
    private bool _isRejecting;

    /// <summary>
    /// Причина отказа из поля ввода.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmReject))]
    [NotifyPropertyChangedFor(nameof(CanRunPrimaryAction))]
    private string _rejectionReason = string.Empty;

    /// <summary>
    /// Ошибка решения; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionError))]
    [NotifyPropertyChangedFor(nameof(ShowApproveActionError))]
    private string? _actionError;

    /// <summary>
    /// Решение отправляется: повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotActing))]
    [NotifyPropertyChangedFor(nameof(CanConfirmReject))]
    [NotifyPropertyChangedFor(nameof(CanRunPrimaryAction))]
    private bool _isActing;

    /// <summary>
    /// Действия видны: карточка загружена, заявка ждёт решения.
    /// </summary>
    public bool ShowDecision => ShowContent && IsPending;

    /// <summary>
    /// Подсказка о последствиях видна: панель причины ещё закрыта.
    /// </summary>
    public bool ShowDecisionHint => ShowDecision && !IsRejecting;

    /// <summary>
    /// Панель причины видна: отказ подтверждается вторым шагом.
    /// </summary>
    public bool ShowReasonPanel => ShowDecision && IsRejecting;

    /// <summary>
    /// Причина отказа видна: заявка отклонена.
    /// </summary>
    public bool ShowRejectionReason => IsRejected;

    /// <summary>
    /// «Рассмотрена» существует только у заявки с решением.
    /// </summary>
    public bool HasReviewedAt => !string.IsNullOrEmpty(ReviewedAtText);

    /// <summary>
    /// «О себе» существует только у заполненного рассказа.
    /// </summary>
    public bool HasAbout => !string.IsNullOrEmpty(AboutText);

    /// <summary>
    /// «О себе» не заполнено: карточка объясняет пустое место, как веб.
    /// </summary>
    public bool HasNoAbout => !HasAbout;

    /// <summary>
    /// Заявка разобрана: карточка говорит, что решение не меняется.
    /// </summary>
    public bool ShowReviewedNote => ShowContent && !IsPending;

    /// <summary>
    /// Подсказка под полем причины: предел и необратимость текста.
    /// </summary>
    [ObservableProperty]
    private string _rejectionReasonHint = string.Empty;

    /// <summary>
    /// Есть ошибка решения.
    /// </summary>
    public bool HasActionError => !string.IsNullOrEmpty(ActionError);

    /// <summary>
    /// Ошибка одобрения видна: отказ объясняет свою причину в панели.
    /// </summary>
    public bool ShowApproveActionError => ShowDecisionHint && HasActionError;

    /// <summary>
    /// Кнопки решения доступны: запрос не в полёте.
    /// </summary>
    public bool IsNotActing => !IsActing;

    /// <summary>
    /// Надпись на главной кнопке: «Одобрить» или подтверждение отказа.
    /// </summary>
    public string PrimaryActionText => IsRejecting ? Loc.T("Отклонить") : Loc.T("Одобрить");

    /// <summary>
    /// Надпись на второй кнопке: «Отклонить» или «Отмена».
    /// </summary>
    public string SecondaryActionText => IsRejecting ? Loc.T("Отмена") : Loc.T("Отклонить");

    /// <summary>
    /// Подтверждение отказа доступно: причина не пустая и не длиннее предела домена.
    /// </summary>
    public bool CanConfirmReject
    {
        get
        {
            var length = RejectionReason.Trim().Length;

            return !IsActing && length > 0 && length <= MaxRejectionReasonLength;
        }
    }

    /// <summary>
    /// Главная кнопка доступна: не идёт запрос, а у отказа причина уже введена.
    /// </summary>
    public bool CanRunPrimaryAction => !IsActing && (!IsRejecting || CanConfirmReject);

    /// <summary>
    /// Завершается, когда цикл автоповтора останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора чтения: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Запоминает заявку, которую показывает страница.
    /// </summary>
    /// <remarks>
    /// Идентификатор приходит из маршрута Shell, а не из ответа: карточка всегда читается
    /// отдельным запросом, локальной копии у неё нет.
    /// </remarks>
    /// <param name="requestId">Идентификатор заявки из маршрута.</param>
    /// <exception cref="ArgumentException">Идентификатор пустой.</exception>
    public void Initialize(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор заявки не может быть пустым.", nameof(requestId));
        }

        _requestId = requestId;
        _hasRequestId = true;
    }

    /// <summary>
    /// Читает заявку: первый раз с индикатором, при возврате — тихо.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_hasRequestId)
        {
            return;
        }

        StopAutoRetryLoop();

        await LoadAsync(_hasLoaded ? LoadMode.Silent : LoadMode.Initial, cancellationToken);
    }

    /// <summary>
    /// Включает автоповтор: страница видна.
    /// </summary>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Выключает автоповтор: страница скрыта.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
    }

    /// <summary>
    /// Сбрасывает экран после завершения сессии.
    /// </summary>
    /// <remarks>
    /// Поколение сдвигается: ответ, который ещё летит, не вернёт карточку прошлого пользователя.
    /// </remarks>
    public void ApplySignedOutState()
    {
        StopAutoRetryLoop();

        _generation++;
        _readFailureKind = ApiFailureKind.None;
        IsPending = false;

        ApplyState(PageState.SignInRequired);
    }

    /// <summary>
    /// Одобряет заявку: сервер меняет роль и завершает сессии заявителя.
    /// </summary>
    /// <remarks>
    /// Пока запрос в полёте, повторное нажатие игнорируется. Ошибка остаётся на экране: успех
    /// возвращает <c>null</c> и страница уходит назад, а отказ — текст причины.
    /// </remarks>
    /// <returns>Текст ошибки либо <c>null</c>, если заявка одобрена.</returns>
    public async Task<string?> ApproveAsync()
    {
        if (IsActing || !IsPending)
        {
            return null;
        }

        IsActing = true;
        ActionError = null;

        try
        {
            await _api.ApproveRequestAsync(_requestId, CancellationToken.None);

            return null;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (BarberSearchApiException exception)
        {
            return FailAction(DescribeActionFailure(exception, isApprove: true));
        }
        catch (HttpRequestException)
        {
            return FailAction(AdminErrorText.Network);
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            return FailAction(AdminErrorText.Network);
        }
        finally
        {
            IsActing = false;
        }
    }

    /// <summary>
    /// Отклоняет заявку с причиной из поля ввода.
    /// </summary>
    /// <remarks>
    /// Причина проверяется до запроса теми же правилами, что у домена: пустая и слишком
    /// длинная не отправляются вовсе. Успех возвращает <c>null</c>, ошибка — текст и остаётся
    /// на экране, чтобы человек видел, что заявка не изменилась.
    /// </remarks>
    /// <returns>Текст ошибки либо <c>null</c>, если заявка отклонена.</returns>
    public async Task<string?> RejectAsync()
    {
        if (IsActing || !IsPending)
        {
            return null;
        }

        var reason = RejectionReason.Trim();

        if (reason.Length == 0)
        {
            return FailAction(Loc.T("Укажите причину отказа."));
        }

        if (reason.Length > MaxRejectionReasonLength)
        {
            return FailAction(Loc.T(
                "Слишком длинная причина — максимум {0} символов.",
                MaxRejectionReasonLength));
        }

        IsActing = true;
        ActionError = null;

        try
        {
            await _api.RejectRequestAsync(_requestId, reason, CancellationToken.None);

            return null;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (BarberSearchApiException exception)
        {
            return FailAction(DescribeActionFailure(exception, isApprove: false));
        }
        catch (HttpRequestException)
        {
            return FailAction(AdminErrorText.Network);
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            return FailAction(AdminErrorText.Network);
        }
        finally
        {
            IsActing = false;
        }
    }

    /// <summary>
    /// Открывает панель причины: отказ подтверждается вторым шагом.
    /// </summary>
    [RelayCommand]
    private void StartRejection()
    {
        if (!IsPending || IsActing)
        {
            return;
        }

        RejectionReason = string.Empty;
        ActionError = null;
        IsRejecting = true;
    }

    /// <summary>
    /// Закрывает панель причины, ничего не меняя.
    /// </summary>
    [RelayCommand]
    private void CancelRejection()
    {
        if (IsActing)
        {
            return;
        }

        IsRejecting = false;
        RejectionReason = string.Empty;
        ActionError = null;
    }

    /// <summary>
    /// Правка причины снимает старую ошибку: она относилась к прежнему тексту.
    /// </summary>
    /// <param name="value">Новый текст причины.</param>
    partial void OnRejectionReasonChanged(string value) => ActionError = null;

    /// <summary>
    /// Повторяет чтение заявки — сразу, по кнопке.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryReadAsync()
    {
        StopAutoRetryLoop();
        ReadError = string.Empty;

        await LoadAsync(LoadMode.Initial, CancellationToken.None);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Читает заявку и раскладывает ответ по карточке.
    /// </summary>
    /// <remarks>
    /// Тихая попытка цикла не стирает причину ошибки до ответа и не показывает индикатор:
    /// иначе карточка мигала бы пустым местом на каждой паузе.
    /// </remarks>
    /// <param name="mode">Показать индикатор, обновить молча или повторить в фоне.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadAsync(LoadMode mode, CancellationToken cancellationToken)
    {
        _generation++;

        var generation = _generation;
        var kind = ApiFailureKind.None;

        if (mode != LoadMode.Retry)
        {
            ReadError = string.Empty;
            _readFailureKind = ApiFailureKind.None;
        }

        if (mode == LoadMode.Initial)
        {
            ApplyState(PageState.Loading);
        }

        try
        {
            var request = await _api.GetRequestAsync(_requestId, cancellationToken);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            ApplyRequest(request);

            _hasLoaded = true;
            _readFailureKind = ApiFailureKind.None;
            ReadError = string.Empty;

            ApplyState(PageState.Content);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            kind = ApiFailureKind.Terminal;

            ReadError = string.Empty;
            _readFailureKind = kind;

            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            kind = ApiFailureKind.Terminal;

            ReadError = string.Empty;
            _readFailureKind = kind;

            if (generation == _generation)
            {
                ApplyState(PageState.Forbidden);
            }
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            kind = ApiFailureKind.Terminal;

            ReadError = string.Empty;
            _readFailureKind = kind;

            if (generation == _generation)
            {
                ApplyState(PageState.NotFound);
            }
        }
        catch (BarberSearchApiException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            ReadError = AdminErrorText.Read(exception, kind, LoadErrorText);
            _readFailureKind = kind;

            if (generation == _generation)
            {
                ApplyState(PageState.Error);
            }
        }
        catch (HttpRequestException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            ReadError = AdminErrorText.Network;
            _readFailureKind = kind;

            if (generation == _generation)
            {
                ApplyState(PageState.Error);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            kind = ApiFailureKind.Transport;

            ReadError = AdminErrorText.Network;
            _readFailureKind = kind;

            if (generation == _generation)
            {
                ApplyState(PageState.Error);
            }
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

            ReadError = LoadErrorText;
            _readFailureKind = kind;

            if (generation == _generation)
            {
                ApplyState(PageState.Error);
            }
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
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private Task<ApiFailureKind> RetryReadAttemptAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ReadError) || !_readFailureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadAsync(LoadMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл, если страница видна, а ошибка чтения повторяемая.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || ShowLoading || ShowForbidden || ShowSignInRequired || ShowNotFound)
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
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Раскладывает заявку по карточке.
    /// </summary>
    /// <remarks>
    /// Даты и стаж собираются здесь, а не в XAML: форматирование живёт в
    /// <see cref="AccountText"/> и <see cref="BarberText"/>, как у остальных экранов.
    /// </remarks>
    /// <param name="request">Заявка из ответа API.</param>
    private void ApplyRequest(AdminBarberRequestResponse request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();

        DisplayNameText = request.DisplayName;
        ApplicantNameText = request.Applicant.FullName;
        ApplicantEmailText = request.Applicant.Email;
        CityText = request.City;
        ExperienceText = BarberText.Experience(request.YearsOfExperience);
        SalonText = request.HasSalon ? Loc.T("Да") : Loc.T("Нет");
        SubmittedAtText = AccountText.Moment(request.CreatedAt, TimeZoneInfo.Local, now);
        ReviewedAtText = request.ReviewedAt is { } reviewedAt
            ? AccountText.Moment(reviewedAt, TimeZoneInfo.Local, now)
            : string.Empty;
        AboutText = request.About ?? string.Empty;
        RejectionReasonText = request.RejectionReason ?? Loc.T("Причина не указана.");
        StatusText = AdminRequestStatus.Text(request.Status);
        IsPending = request.Status == BarberRequestStatus.Pending;
        IsApproved = request.Status == BarberRequestStatus.Approved;
        IsRejected = request.Status == BarberRequestStatus.Rejected;
    }

    /// <summary>
    /// Показывает ровно одно состояние страницы.
    /// </summary>
    /// <param name="state">Состояние, которое должно остаться единственным видимым.</param>
    private void ApplyState(PageState state)
    {
        ShowLoading = state == PageState.Loading;
        ShowContent = state == PageState.Content;
        ShowError = state == PageState.Error;
        ShowNotFound = state == PageState.NotFound;
        ShowForbidden = state == PageState.Forbidden;
        ShowSignInRequired = state == PageState.SignInRequired;
    }

    /// <summary>
    /// Запоминает текст ошибки решения и возвращает его странице.
    /// </summary>
    /// <param name="message">Текст для человека.</param>
    /// <returns>Тот же текст.</returns>
    private string? FailAction(string message)
    {
        ActionError = message;

        return message;
    }

    /// <summary>
    /// Переводит отказ решения в текст, не показывая английский <c>detail</c>.
    /// </summary>
    /// <remarks>
    /// <c>409</c> значит, что заявку уже рассмотрели, а <c>invalidApplicant</c> — что заявитель
    /// перестал быть клиентом. В обоих случаях помочь может только повторное чтение очереди.
    /// </remarks>
    /// <param name="exception">Отказ сервера.</param>
    /// <param name="isApprove"><c>true</c> — одобрение, <c>false</c> — отказ.</param>
    /// <returns>Текст для человека.</returns>
    private static string DescribeActionFailure(
        BarberSearchApiException exception,
        bool isApprove)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (HasCode(exception, InvalidApplicantCode))
        {
            return Loc.T(
                "Роль заявителя уже изменилась — заявку нельзя одобрить автоматически. Обновите список.");
        }

        if (exception.StatusCode == HttpStatusCode.NotFound || HasCode(exception, NotFoundCode))
        {
            return Loc.T("Заявка больше не существует. Обновите список.");
        }

        if (HasCode(exception, InvalidStatusCode) || exception.StatusCode == HttpStatusCode.Conflict)
        {
            return Loc.T("Заявку уже рассмотрели. Обновите список.");
        }

        if (HasCode(exception, ReasonRequiredCode))
        {
            return Loc.T("Укажите причину отказа.");
        }

        if (HasCode(exception, ReasonTooLongCode))
        {
            return Loc.T(
                "Слишком длинная причина — максимум {0} символов.",
                MaxRejectionReasonLength);
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            return AdminErrorText.Forbidden;
        }

        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return AdminErrorText.TooManyRequests;
        }

        return isApprove ? ApproveFailedText : RejectFailedText;
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
    /// Гасит сессию: пара мертва, карточка показывает состояние входа.
    /// </summary>
    /// <returns>Задача очистки.</returns>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Состояние карточки: ровно одно из шести.
    /// </summary>
    private enum PageState
    {
        /// <summary>
        /// Заявка читается: индикатор вместо карточки.
        /// </summary>
        Loading,

        /// <summary>
        /// Заявка загружена: анкета и (у ожидающей) решение.
        /// </summary>
        Content,

        /// <summary>
        /// Чтение не удалось: причина и «Повторить».
        /// </summary>
        Error,

        /// <summary>
        /// Заявки нет: ссылка устарела.
        /// </summary>
        NotFound,

        /// <summary>
        /// Роль не администратор: повторять нечего.
        /// </summary>
        Forbidden,

        /// <summary>
        /// Пара токенов мертва: нужен вход.
        /// </summary>
        SignInRequired,
    }

    /// <summary>
    /// Режим чтения: показать индикатор, обновить молча или повторить в фоне.
    /// </summary>
    private enum LoadMode
    {
        /// <summary>
        /// Первое чтение: экран показывает индикатор.
        /// </summary>
        Initial,

        /// <summary>
        /// Тихое чтение при возврате на страницу.
        /// </summary>
        Silent,

        /// <summary>
        /// Попытка цикла автоповтора: ошибка не стирается до ответа.
        /// </summary>
        Retry,
    }
}
