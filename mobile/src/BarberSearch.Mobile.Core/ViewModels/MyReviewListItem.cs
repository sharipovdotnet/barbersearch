using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Строка списка «Мои отзывы»: ответ API, разложенный по текстам карточки.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MyReviewResponse"/> несёт всё для строки — оценку, текст, статус, барбера и
/// визит, поэтому список не догружает ни карточку барбера, ни бронь. Пояс салона
/// разрешается один раз в конструкторе: <c>visit.timeZoneId</c> не меняется, а перевод
/// времени нужен при каждой смене оценки или текста.
/// </para>
/// <para>
/// Статус публикации берётся только из ответа сервера, в том числе после правки: правило
/// «правка текста возвращает на модерацию» живёт на сервере, и повторять его на клиенте
/// значило бы врать до перезагрузки кабинета.
/// </para>
/// </remarks>
public sealed partial class MyReviewListItem : ObservableObject
{
    private readonly DateTimeOffset _createdAt;

    private DateTimeOffset _updatedAt;

    /// <summary>
    /// Создаёт строку из ответа API.
    /// </summary>
    /// <param name="review">Отзыв из списка «мои отзывы».</param>
    public MyReviewListItem(MyReviewResponse review)
    {
        ArgumentNullException.ThrowIfNull(review);

        _createdAt = review.CreatedAt;
        _updatedAt = review.UpdatedAt;

        Id = review.Id;
        TimeZone = SalonClock.TryResolve(review.Visit.TimeZoneId);
        VisitStartAt = review.Visit.StartAt;
        BarberId = review.Barber.Id;
        BarberName = review.Barber.DisplayName;
        VisitText = ReviewText.VisitLine(review.Visit, TimeZone);

        ApplyValues(review.Rating, review.Text, review.IsPublished);
    }

    /// <summary>
    /// Идентификатор отзыва.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Профиль барбера — для перехода на его карточку.
    /// </summary>
    public Guid BarberId { get; }

    /// <summary>
    /// Имя барбера из ответа.
    /// </summary>
    public string BarberName { get; }

    /// <summary>
    /// Пояс салона визита; <c>null</c> — UTC.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; }

    /// <summary>
    /// Начало визита в UTC — для формы правки, которой нужен момент без запроса.
    /// </summary>
    public DateTimeOffset VisitStartAt { get; }

    /// <summary>
    /// Когда отзыв создан.
    /// </summary>
    public DateTimeOffset CreatedAt => _createdAt;

    /// <summary>
    /// Когда отзыв последний раз менялся.
    /// </summary>
    public DateTimeOffset UpdatedAt => _updatedAt;

    /// <summary>
    /// Оценка от 1 до 5.
    /// </summary>
    [ObservableProperty]
    private int _rating;

    /// <summary>
    /// Текст отзыва; <c>null</c> — оценки без комментария.
    /// </summary>
    [ObservableProperty]
    private string? _text;

    /// <summary>
    /// Опубликован ли отзыв; <c>false</c> — «на модерации».
    /// </summary>
    [ObservableProperty]
    private bool _isPublished;

    /// <summary>
    /// Подпись статуса: «Опубликован» или «На модерации».
    /// </summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Звёзды одной строкой: «★★★★☆».
    /// </summary>
    [ObservableProperty]
    private string _stars = string.Empty;

    /// <summary>
    /// Оценка числом: «5 / 5».
    /// </summary>
    [ObservableProperty]
    private string _ratingText = string.Empty;

    /// <summary>
    /// Дата отзыва в поясе салона: «15 ноября 2026».
    /// </summary>
    [ObservableProperty]
    private string _reviewDateText = string.Empty;

    /// <summary>
    /// Строка визита: момент, услуга, салон и цена.
    /// </summary>
    [ObservableProperty]
    private string _visitText = string.Empty;

    /// <summary>
    /// Тело отзыва либо «Без комментария.».
    /// </summary>
    [ObservableProperty]
    private string _bodyText = string.Empty;

    /// <summary>
    /// Идёт удаление этой строки: повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDeleting))]
    private bool _isDeleting;

    /// <summary>
    /// Кнопки строки доступны: удаление не идёт.
    /// </summary>
    public bool IsNotDeleting => !IsDeleting;

    /// <summary>
    /// Применяет результат правки: новую оценку, текст и статус из ответа.
    /// </summary>
    /// <remarks>
    /// Дата создания не меняется — строка остаётся на своём месте в списке. Дата правки
    /// берётся из ответа, а статус публикации — только из него: сервер знает, вернулся
    /// ли отзыв на проверку.
    /// </remarks>
    /// <param name="response">Ответ <c>PUT</c> с новым статусом и временем правки.</param>
    /// <param name="rating">Оценка, которую отправили.</param>
    /// <param name="text">Текст, который отправили; <c>null</c> — без комментария.</param>
    public void ApplyUpdated(UpdateReviewResponse response, int rating, string? text)
    {
        ArgumentNullException.ThrowIfNull(response);

        _updatedAt = response.UpdatedAt;

        ApplyValues(rating, text, response.IsPublished);
        OnPropertyChanged(nameof(UpdatedAt));
    }

    /// <summary>
    /// Раскладывает значения по наблюдаемым свойствам.
    /// </summary>
    private void ApplyValues(int rating, string? text, bool isPublished)
    {
        Rating = rating;
        Text = text;
        IsPublished = isPublished;
        StatusText = ReviewText.Status(isPublished);
        Stars = ReviewText.Stars(rating);
        RatingText = ReviewText.Rating(rating);
        ReviewDateText = ReviewText.ReviewDate(_createdAt, TimeZone);
        BodyText = ReviewText.Body(text);
    }
}
