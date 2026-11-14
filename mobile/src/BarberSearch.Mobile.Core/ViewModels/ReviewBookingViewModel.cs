using System.Globalization;
using System.Net;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Отзыв о визите»: пять звёзд, комментарий и отправка.
/// </summary>
/// <remarks>
/// <para>
/// Отзыв обязателен только в части оценки: без неё запрос не уходит, а кнопка остаётся
/// доступной — ошибка показывается после нажатия, как в вебе. Текст обрезается по краям,
/// пустой уходит как <c>null</c>, длина ограничена 2000 символами и сервером, и счётчиком.
/// </para>
/// <para>
/// Отзыв создаётся скрытым и уходит на модерацию, поэтому экран прямо говорит, что он
/// появится на карточке барбера после проверки. Ошибки разбираются по публичным кодам:
/// порядок — коды, потом <c>409</c>, потом <c>403</c>; веб проверяет <c>409</c> раньше кода
/// <c>bookingNotCompleted</c> и на нём соврал бы.
/// </para>
/// </remarks>
public sealed partial class ReviewBookingViewModel : ObservableObject
{
    private const string BookingNotCompletedCode = "review.create.bookingNotCompleted";
    private const string RatingCode = "review.rating";
    private const string TextTooLongCode = "review.text.tooLong";

    private const string RatingRequiredText = "Поставьте оценку от 1 до 5.";
    private const string BookingNotCompletedText = "Отзыв можно оставить только после завершённого визита.";
    private const string RatingInvalidText = "Оценка должна быть от 1 до 5.";
    private const string TextTooLongText = "Слишком длинный текст: максимум 2000 символов.";
    private const string AlreadyReviewedText = "По этой записи уже есть отзыв — второй оставить нельзя.";
    private const string ForbiddenText = "Оставить отзыв может только клиент этой записи.";
    private const string SubmitFailedText = "Не удалось отправить отзыв.";
    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string TooManyRequestsText =
        "Слишком много запросов. Подождите минуту и повторите.";
    private const string SessionExpiredText = "Сессия истекла — войдите снова.";

    private readonly IReviewsApi _reviewsApi;

    private BookingListItem? _item;

    /// <summary>
    /// Создаёт экран отзыва.
    /// </summary>
    /// <param name="reviewsApi">Создание отзыва.</param>
    public ReviewBookingViewModel(IReviewsApi reviewsApi)
    {
        ArgumentNullException.ThrowIfNull(reviewsApi);

        _reviewsApi = reviewsApi;
    }

    /// <summary>
    /// Отзыв отправлен: страница отмечает его в строке списка и закрывается.
    /// </summary>
    public event EventHandler? Submitted;

    /// <summary>
    /// Имя барбера из карточки: по кому отзыв.
    /// </summary>
    [ObservableProperty]
    private string _barberName = BookingText.BarberFallback;

    /// <summary>
    /// Момент визита: «сб, 15 ноября в 18:30».
    /// </summary>
    [ObservableProperty]
    private string _momentText = string.Empty;

    /// <summary>
    /// Оценка от 1 до 5; ноль — не выбрана.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRating))]
    [NotifyPropertyChangedFor(nameof(Star1Selected))]
    [NotifyPropertyChangedFor(nameof(Star2Selected))]
    [NotifyPropertyChangedFor(nameof(Star3Selected))]
    [NotifyPropertyChangedFor(nameof(Star4Selected))]
    [NotifyPropertyChangedFor(nameof(Star5Selected))]
    [NotifyPropertyChangedFor(nameof(RatingText))]
    private int _rating;

    /// <summary>
    /// Комментарий к отзыву.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextCounterText))]
    private string _text = string.Empty;

    /// <summary>
    /// Идёт отправка: кнопка неактивна, повтор игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    private bool _isSubmitting;

    /// <summary>
    /// Ошибка отправки; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Максимальная длина комментария.
    /// </summary>
    public int MaxTextLength { get; } = BookingText.ReviewTextMaxLength;

    /// <summary>
    /// Есть ли выбранная оценка.
    /// </summary>
    public bool HasRating => Rating > 0;

    /// <summary>
    /// Подпись оценки: «4 / 5» либо «не выбрана».
    /// </summary>
    public string RatingText =>
        HasRating
            ? string.Concat(Rating.ToString(CultureInfo.InvariantCulture), " / 5")
            : "не выбрана";

    /// <summary>
    /// Счётчик комментария: «0 / 2000».
    /// </summary>
    public string TextCounterText => string.Concat(
        Text.Length.ToString(CultureInfo.InvariantCulture),
        " / ",
        MaxTextLength.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Можно ли нажать «Отправить отзыв»: кнопка остаётся доступной и без оценки.
    /// </summary>
    public bool CanSubmit => !IsSubmitting;

    /// <summary>
    /// Надпись на кнопке: во время запроса — «Отправляем…».
    /// </summary>
    public string SubmitText => IsSubmitting ? "Отправляем…" : "Отправить отзыв";

    /// <summary>
    /// Есть ли ошибка, которую надо показать.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Первая звезда закрашена.
    /// </summary>
    public bool Star1Selected => Rating >= 1;

    /// <summary>
    /// Вторая звезда закрашена.
    /// </summary>
    public bool Star2Selected => Rating >= 2;

    /// <summary>
    /// Третья звезда закрашена.
    /// </summary>
    public bool Star3Selected => Rating >= 3;

    /// <summary>
    /// Четвёртая звезда закрашена.
    /// </summary>
    public bool Star4Selected => Rating >= 4;

    /// <summary>
    /// Пятая звезда закрашена.
    /// </summary>
    public bool Star5Selected => Rating >= 5;

    /// <summary>
    /// Заполняет экран данными визита.
    /// </summary>
    /// <param name="item">Строка списка, по которой оставляют отзыв.</param>
    public void Initialize(BookingListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _item = item;
        BarberName = item.BarberName;
        MomentText = item.MomentText;
    }

    /// <summary>
    /// Ставит оценку; повторное нажатие на ту же звезду снимает её.
    /// </summary>
    /// <param name="value">Оценка от 1 до 5.</param>
    [RelayCommand]
    private void SelectRating(int value)
    {
        if (value is < 1 or > 5)
        {
            return;
        }

        Rating = Rating == value ? 0 : value;

        if (Rating > 0)
        {
            ErrorMessage = null;
        }
    }

    /// <summary>
    /// Отправляет отзыв.
    /// </summary>
    /// <remarks>
    /// Без оценки запрос не уходит: проверка на клиенте экономит лимит запросов, а сервер
    /// всё равно ответил бы <c>review.rating</c>. Текст обрезается по краям, пустой уходит
    /// как <c>null</c>.
    /// </remarks>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSubmitting || _item is null)
        {
            return;
        }

        if (Rating is < 1 or > 5)
        {
            ErrorMessage = RatingRequiredText;

            return;
        }

        var text = Text.Trim();

        if (text.Length > MaxTextLength)
        {
            ErrorMessage = TextTooLongText;

            return;
        }

        IsSubmitting = true;
        ErrorMessage = null;

        try
        {
            await _reviewsApi.CreateReviewAsync(_item.Id, Rating, text.Length == 0 ? null : text);

            Submitted?.Invoke(this, EventArgs.Empty);
        }
        catch (BarberSearchApiException exception)
        {
            ErrorMessage = DescribeReviewFailure(exception);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = NetworkErrorText;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            ErrorMessage = NetworkErrorText;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Переводит ошибку отзыва в текст.
    /// </summary>
    /// <remarks>
    /// Порядок ветвлений: сначала публичные коды, потом <c>409</c>, потом <c>403</c>. Так
    /// <c>bookingNotCompleted</c> не подменяется общим «отзыв уже есть», а повтор на
    /// уникальном индексе всё равно получает понятное объяснение.
    /// </remarks>
    private static string DescribeReviewFailure(BarberSearchApiException exception)
    {
        if (HasCode(exception, BookingNotCompletedCode))
        {
            return BookingNotCompletedText;
        }

        if (HasCode(exception, RatingCode))
        {
            return RatingInvalidText;
        }

        if (HasCode(exception, TextTooLongCode))
        {
            return TextTooLongText;
        }

        if (exception.StatusCode == HttpStatusCode.Conflict)
        {
            return AlreadyReviewedText;
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            return ForbiddenText;
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => TooManyRequestsText,
            HttpStatusCode.Unauthorized => SessionExpiredText,
            _ => SubmitFailedText,
        };
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);
}
