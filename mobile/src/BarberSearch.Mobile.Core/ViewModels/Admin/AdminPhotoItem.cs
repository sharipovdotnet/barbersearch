using BarberSearch.Contracts.Admin;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Строка очереди премодерации: кадр, его владелец и два решения.
/// </summary>
/// <remarks>
/// <para>
/// Один тип на обе очереди, потому что карточка у них одна: кадр, имя владельца, подпись
/// и решения. Различаются только поля ответа — у аватара это пользователь с email,
/// у портфолио профиль мастера с подписью кадра, — поэтому вид строки хранит
/// <see cref="IsPortfolio"/> и профиль нужен только второму.
/// </para>
/// <para>
/// Решения живут на строке, но выполняет их <see cref="AdminPhotoModerationList"/>: карточка
/// показывает состояние «решаем», а очередь держит запрос, обновление списка и тексты
/// сбоев. Так строка не знает ни про API, ни про то, что список придётся перечитать.
/// </para>
/// <para>
/// Отказ требует причины: подтверждение доступно, только пока причина не пуста после
/// <c>Trim</c> и не длиннее 500 символов — ровно столько же, сколько проверяет сервер
/// (<c>RejectPhotoRequest.Reason</c>). Длина в поле ограничена тем же числом, поэтому
/// «слишком длинную» причину ввести нельзя, а непустую сервер увидит уже обрезанной.
/// </para>
/// </remarks>
public sealed partial class AdminPhotoItem : ObservableObject
{
    /// <summary>
    /// Предел причины отказа: тот же, что в домене; длиннее сервер ответит <c>400</c>.
    /// </summary>
    public const int RejectionReasonMaxLength = 500;

    private readonly AdminPhotoModerationList _queue;

    private bool _isApproving;

    private AdminPhotoItem(
        AdminPhotoModerationList queue,
        Guid id,
        Guid? barberProfileId,
        string ownerName,
        string email,
        string? description,
        string imageUrl,
        PhotoModerationStatus moderationStatus,
        DateTimeOffset submittedAt)
    {
        _queue = queue;
        Id = id;
        BarberProfileId = barberProfileId;
        OwnerName = ownerName;
        Email = email;
        Description = description;
        ImageUrl = imageUrl;
        StatusText = StatusLabel(moderationStatus);
        SubmittedText = Loc.T(
            "ждёт с {0}",
            submittedAt.ToLocalTime().ToString("d MMM HH:mm", Loc.Culture));
        ActionTarget = description is null || description.Length == 0
            ? ownerName
            : string.Concat(ownerName, ": ", description);
        AltText = Loc.T("Фото: {0}", ActionTarget);
    }

    /// <summary>
    /// Идентификатор строки: пользователь у аватара, фотография у портфолио.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Профиль мастера у работы портфолио; <c>null</c> у аватара.
    /// </summary>
    public Guid? BarberProfileId { get; }

    /// <summary>
    /// Очередь, к которой относится строка.
    /// </summary>
    public AdminPhotosSegment Segment => _queue.Segment;

    /// <summary>
    /// Работа портфолио, а не аватар: у неё есть подпись и профиль мастера.
    /// </summary>
    public bool IsPortfolio => Segment == AdminPhotosSegment.Portfolio;

    /// <summary>
    /// Имя владельца кадра: имя пользователя либо отображаемое имя мастера.
    /// </summary>
    public string OwnerName { get; }

    /// <summary>
    /// Email владельца кадра: админское чтение, по нему поддержка ищет человека.
    /// </summary>
    public string Email { get; }

    /// <summary>
    /// Есть ли email для показа.
    /// </summary>
    public bool HasEmail => Email.Length > 0;

    /// <summary>
    /// Подпись к кадру портфолио; <c>null</c> у аватара и у кадра без подписи.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Есть ли подпись к кадру.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// Абсолютный адрес кадра: миниатюра своего размера, иначе исходник.
    /// </summary>
    public string ImageUrl { get; }

    /// <summary>
    /// Описание кадра для экранного диктора.
    /// </summary>
    public string AltText { get; }

    /// <summary>
    /// Состояние проверки словом: в очереди всегда «На проверке».
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Сколько кадр ждёт решения: «ждёт с 20 нояб. 14:05».
    /// </summary>
    public string SubmittedText { get; }

    /// <summary>
    /// Что именно отклоняют: имя владельца, а у портфолио — имя с подписью кадра.
    /// </summary>
    public string ActionTarget { get; }

    /// <summary>
    /// Подсказка экранному диктору для кнопки одобрения: «Одобрить: {цель}».
    /// </summary>
    public string ApproveHint => Loc.T("Одобрить: {0}", ActionTarget);

    /// <summary>
    /// Подсказка экранному диктору для кнопки отказа: «Отклонить: {цель}».
    /// </summary>
    public string RejectHint => Loc.T("Отклонить: {0}", ActionTarget);

    /// <summary>
    /// Панель причины отказа открыта.
    /// </summary>
    [ObservableProperty]
    private bool _isRejectOpen;

    /// <summary>
    /// Причина отказа, введённая администратором.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmReject))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRejectCommand))]
    private string? _reason = string.Empty;

    /// <summary>
    /// Идёт решение по этому кадру: кнопки карточки гаснут.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDecide))]
    [NotifyPropertyChangedFor(nameof(IsNotActing))]
    [NotifyPropertyChangedFor(nameof(ApproveText))]
    [NotifyPropertyChangedFor(nameof(RejectText))]
    [NotifyPropertyChangedFor(nameof(ConfirmRejectText))]
    [NotifyPropertyChangedFor(nameof(CanConfirmReject))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRejectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRejectCommand))]
    private bool _isActing;

    /// <summary>
    /// Сбой действия, показанный рядом с карточкой; <c>null</c> — сбоя нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Байты превью, уже прочитанные приложением; <c>null</c> — превью ещё нет.
    /// </summary>
    /// <remarks>
    /// Адрес в <see cref="ImageUrl"/> закрыт ролью: загрузчик картинок платформы ходит без
    /// заголовков и получает <c>404</c> на неодобренный кадр. Поэтому байты приносит
    /// <c>IAdminMediaApi</c> тем же авторизованным клиентом, что и списки, а картинка
    /// собирается из памяти. Кэша нет: байты живут на строке и уходят вместе с ней.
    /// </remarks>
    [ObservableProperty]
    private byte[]? _imageBytes;

    /// <summary>
    /// Превью не удалось показать: вместо кадра остаётся подложка, карточка рабочая.
    /// </summary>
    /// <remarks>
    /// Попытка одна: превью — украшение, а не данные, и повторять его в фоне незачем.
    /// </remarks>
    [ObservableProperty]
    private bool _imageFailed;

    /// <summary>
    /// Решение ещё не принято: кнопки карточки доступны.
    /// </summary>
    public bool CanDecide => !IsActing;

    /// <summary>
    /// Кнопки карточки доступны: решение не идёт.
    /// </summary>
    public bool IsNotActing => !IsActing;

    /// <summary>
    /// Есть ли сбой действия для показа.
    /// </summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Подпись кнопки одобрения: во время запроса — «Одобряем…».
    /// </summary>
    public string ApproveText => IsActing && _isApproving
        ? Loc.T("Одобряем…")
        : Loc.T("Одобрить");

    /// <summary>
    /// Подпись кнопки отказа: во время запроса — «Отклоняем…».
    /// </summary>
    public string RejectText => IsActing && !_isApproving
        ? Loc.T("Отклоняем…")
        : Loc.T("Отклонить");

    /// <summary>
    /// Подпись подтверждения отказа: во время запроса — «Отклоняем…».
    /// </summary>
    public string ConfirmRejectText => IsActing && !_isApproving
        ? Loc.T("Отклоняем…")
        : Loc.T("Отклонить");

    /// <summary>
    /// Причина корректна: не пуста после <c>Trim</c> и не длиннее предела домена.
    /// </summary>
    public bool CanConfirmReject =>
        !IsActing
        && (Reason ?? string.Empty).Trim() is { Length: > 0 } reason
        && reason.Length <= RejectionReasonMaxLength;

    /// <summary>
    /// Собирает строку очереди аватаров.
    /// </summary>
    /// <param name="queue">Очередь, которой принадлежит строка.</param>
    /// <param name="photo">Строка ответа API.</param>
    /// <param name="baseAddress">Базовый адрес API для склейки относительных URL.</param>
    /// <returns>Строка для карточки.</returns>
    public static AdminPhotoItem FromAvatar(
        AdminPhotoModerationList queue,
        AdminAvatarPhotoResponse photo,
        Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var name = string.Concat(photo.FirstName, " ", photo.LastName).Trim();

        return new AdminPhotoItem(
            queue,
            photo.UserId,
            barberProfileId: null,
            name.Length == 0 ? photo.Email : name,
            photo.Email,
            description: null,
            MediaUrl.Resolve(baseAddress, MediaUrl.Resize(photo.AvatarUrl, "md")),
            photo.ModerationStatus,
            photo.SubmittedAt);
    }

    /// <summary>
    /// Собирает строку очереди портфолио.
    /// </summary>
    /// <param name="queue">Очередь, которой принадлежит строка.</param>
    /// <param name="photo">Строка ответа API.</param>
    /// <param name="baseAddress">Базовый адрес API для склейки относительных URL.</param>
    /// <returns>Строка для карточки.</returns>
    public static AdminPhotoItem FromPortfolio(
        AdminPhotoModerationList queue,
        AdminPortfolioPhotoResponse photo,
        Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(baseAddress);

        return new AdminPhotoItem(
            queue,
            photo.PhotoId,
            photo.BarberProfileId,
            photo.BarberDisplayName,
            photo.OwnerEmail,
            photo.Description,
            MediaUrl.Resolve(baseAddress, MediaUrl.Resize(photo.Url, "md")),
            photo.ModerationStatus,
            photo.SubmittedAt);
    }

    /// <summary>
    /// Одобряет кадр.
    /// </summary>
    /// <returns>Задача решения.</returns>
    [RelayCommand(CanExecute = nameof(CanDecide))]
    private Task ApproveAsync() => _queue.ApproveAsync(this);

    /// <summary>
    /// Открывает панель причины отказа, закрывая панель у других строк.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDecide))]
    private void OpenReject() => _queue.OpenReject(this);

    /// <summary>
    /// Закрывает панель причины без отказа.
    /// </summary>
    [RelayCommand]
    private void CancelReject() => _queue.CloseReject(this);

    /// <summary>
    /// Отклоняет кадр с введённой причиной.
    /// </summary>
    /// <returns>Задача решения.</returns>
    [RelayCommand(CanExecute = nameof(CanConfirmReject))]
    private Task ConfirmRejectAsync() => _queue.RejectAsync(this);

    /// <summary>
    /// Помечает карточку «решаем» и называет действие, чтобы кнопки сказали правду.
    /// </summary>
    /// <param name="isApproving"><c>true</c> — одобрение, <c>false</c> — отказ.</param>
    internal void BeginActing(bool isApproving)
    {
        _isApproving = isApproving;

        ErrorMessage = null;
        IsActing = true;

        OnPropertyChanged(nameof(ApproveText));
        OnPropertyChanged(nameof(RejectText));
        OnPropertyChanged(nameof(ConfirmRejectText));
    }

    /// <summary>
    /// Снимает пометку «решаем»: запрос закончился.
    /// </summary>
    internal void EndActing() => IsActing = false;

    /// <summary>
    /// Показывает причину отказа и очищает прошлую причину: новое решение — новый ввод.
    /// </summary>
    internal void ShowRejectPanel()
    {
        Reason = string.Empty;
        ErrorMessage = null;
        IsRejectOpen = true;
    }

    /// <summary>
    /// Закрывает панель причины и очищает ввод.
    /// </summary>
    internal void HideRejectPanel()
    {
        IsRejectOpen = false;
        Reason = string.Empty;
        ErrorMessage = null;
    }

    /// <summary>
    /// Показывает сбой действия рядом с карточкой.
    /// </summary>
    /// <param name="message">Текст для человека.</param>
    internal void ApplyError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ErrorMessage = message;
    }

    /// <summary>
    /// Подпись состояния проверки по значению домена.
    /// </summary>
    /// <param name="status">Состояние проверки из ответа.</param>
    /// <returns>Русская подпись веба.</returns>
    private static string StatusLabel(PhotoModerationStatus status) => status switch
    {
        PhotoModerationStatus.Approved => Loc.T("Одобрено"),
        PhotoModerationStatus.Rejected => Loc.T("Отклонено"),
        _ => Loc.T("На проверке"),
    };
}
