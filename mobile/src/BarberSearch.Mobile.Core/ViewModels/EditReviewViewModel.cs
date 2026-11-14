using System.Globalization;
using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Изменить отзыв»: оценка, комментарий и сохранение.
/// </summary>
/// <remarks>
/// <para>
/// Строка списка приходит объектом маршрута: эндпоинта «получить отзыв по id» нет, а всё
/// нужное для формы — оценка, текст, статус — уже есть в списке. Поэтому экран ничего не
/// читает, только пишет.
/// </para>
/// <para>
/// Статус публикации экран не вычисляет: правка только оценки оставляет отзыв
/// опубликованным, правка текста возвращает его на проверку, но правило живёт на сервере.
/// Клиент показывает статус из ответа <c>PUT</c>.
/// </para>
/// </remarks>
public sealed partial class EditReviewViewModel : ObservableObject
{
    private const string RatingInvalidText = "Оценка должна быть от 1 до 5.";
    private const string TextTooLongText = "Слишком длинный текст: максимум 2000 символов.";
    private const string ConflictText = "Отзыв изменился в другой вкладке — обновите список.";
    private const string NotFoundText = "Отзыв больше не существует.";
    private const string ForbiddenText = "Править отзыв может только его автор.";
    private const string SaveFailedText = "Не удалось сохранить отзыв.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string TooManyRequestsText = "Слишком много запросов. Подождите минуту и повторите.";

    private const string HintsPublishedChangedText =
        "Изменение текста вернёт отзыв на проверку: до одобрения он не покажется на карточке барбера.";
    private const string HintsPublishedUnchangedText =
        "Оценку можно изменить сразу: повторная проверка нужна только для текста.";
    private const string HintsModerationText =
        "Отзыв на проверке: изменения появятся на карточке после одобрения.";

    private const string RatingCode = "review.update.rating";
    private const string TextTooLongCode = "review.update.text.tooLong";
    private const string ForbiddenCode = "auth.forbidden";

    private readonly IReviewsApi _reviewsApi;
    private readonly TokenSession _session;

    private MyReviewListItem? _item;
    private bool _isPublished;
    private string _originalText = string.Empty;

    /// <summary>
    /// Создаёт экран правки отзыва.
    /// </summary>
    /// <param name="reviewsApi">Правка отзыва.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    public EditReviewViewModel(IReviewsApi reviewsApi, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(reviewsApi);
        ArgumentNullException.ThrowIfNull(session);

        _reviewsApi = reviewsApi;
        _session = session;
    }

    /// <summary>
    /// Отзыв сохранён: страница применяет ответ к строке владельца и закрывается.
    /// </summary>
    public event EventHandler<UpdateReviewResponse>? Saved;

    /// <summary>
    /// Имя барбера: по кому отзыв.
    /// </summary>
    [ObservableProperty]
    private string _barberName = BookingText.BarberFallback;

    /// <summary>
    /// Момент визита: «15 ноября 2026 в 18:30».
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
    [NotifyPropertyChangedFor(nameof(StatusHint))]
    private string _text = string.Empty;

    /// <summary>
    /// Идёт сохранение: повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool _isSaving;

    /// <summary>
    /// Ошибка сохранения; <c>null</c>, когда её нет.
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
    /// Можно ли нажать «Сохранить».
    /// </summary>
    public bool CanSave => !IsSaving;

    /// <summary>
    /// Надпись на кнопке сохранения.
    /// </summary>
    public string SaveButtonText => IsSaving ? "Сохраняем…" : "Сохранить";

    /// <summary>
    /// Счётчик комментария: «0 / 2000».
    /// </summary>
    public string TextCounterText => string.Concat(
        Text.Length.ToString(CultureInfo.InvariantCulture),
        " / ",
        MaxTextLength.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Подпись оценки: «4 / 5».
    /// </summary>
    public string RatingText => string.Concat(
        Rating.ToString(CultureInfo.InvariantCulture),
        " / 5");

    /// <summary>
    /// Есть ли ошибка, которую надо показать.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Подсказка по статусу: что произойдёт с отзывом после сохранения.
    /// </summary>
    /// <remarks>
    /// Показывается ровно одна подсказка: для отзыва на проверке — что изменения появятся
    /// после одобрения, для опубликованного — меняется ли текст. Правка одной оценки
    /// проверку не запускает.
    /// </remarks>
    public string StatusHint => !_isPublished
        ? HintsModerationText
        : IsTextChanged
            ? HintsPublishedChangedText
            : HintsPublishedUnchangedText;

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
    /// Последний отправленный текст; <c>null</c> — без комментария.
    /// </summary>
    public string? NormalizedText { get; private set; }

    /// <summary>
    /// Изменён ли текст относительно исходного.
    /// </summary>
    private bool IsTextChanged => Text.Trim() != _originalText.Trim();

    /// <summary>
    /// Заполняет экран данными строки списка.
    /// </summary>
    /// <param name="item">Строка «Моих отзывов».</param>
    public void Initialize(MyReviewListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _item = item;
        _isPublished = item.IsPublished;
        _originalText = item.Text ?? string.Empty;

        BarberName = item.BarberName;
        MomentText = ReviewText.VisitMoment(item.VisitStartAt, item.TimeZone);
        Rating = item.Rating;
        Text = _originalText;

        OnPropertyChanged(nameof(StatusHint));
    }

    /// <summary>
    /// Ставит оценку.
    /// </summary>
    /// <param name="value">Оценка от 1 до 5.</param>
    [RelayCommand]
    private void SelectRating(int value)
    {
        if (value is < 1 or > 5)
        {
            return;
        }

        Rating = value;
        ErrorMessage = null;
    }

    /// <summary>
    /// Сохраняет правку.
    /// </summary>
    /// <remarks>
    /// Текст обрезается по краям, пустой уходит как <c>null</c>. Ошибки разбираются в порядке
    /// веба: <c>409</c>/<c>concurrencyConflict</c>, <c>404</c>, <c>403</c>, затем коды оценки
    /// и длины текста.
    /// </remarks>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving || _item is null)
        {
            return;
        }

        if (Rating is < 1 or > 5)
        {
            ErrorMessage = RatingInvalidText;

            return;
        }

        var text = Text.Trim();

        if (text.Length > MaxTextLength)
        {
            ErrorMessage = TextTooLongText;

            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            var response = await _reviewsApi.UpdateReviewAsync(
                _item.Id,
                Rating,
                text.Length == 0 ? null : text);

            NormalizedText = text.Length == 0 ? null : text;
            Saved?.Invoke(this, response);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            ErrorMessage = DescribeFailure(exception);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = NetworkText;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            ErrorMessage = NetworkText;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Переводит отказ правки в текст по статусу и публичным кодам.
    /// </summary>
    private static string DescribeFailure(BarberSearchApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.Conflict
            || HasCode(exception, ApiErrorCodes.Review.UpdateConcurrencyConflict))
        {
            return ConflictText;
        }

        if (exception.StatusCode == HttpStatusCode.NotFound
            || HasCode(exception, ApiErrorCodes.Review.NotFound))
        {
            return NotFoundText;
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden
            || HasCode(exception, ForbiddenCode))
        {
            return ForbiddenText;
        }

        if (HasCode(exception, RatingCode))
        {
            return RatingInvalidText;
        }

        if (HasCode(exception, TextTooLongCode))
        {
            return TextTooLongText;
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => TooManyRequestsText,
            _ => SaveFailedText,
        };
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Гасит сессию: пара мертва, страница вернёт на вкладку «Профиль».
    /// </summary>
    private async Task EndSessionAsync()
    {
        await _session.ClearAsync(CancellationToken.None);
    }
}
