using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Строка очереди отзывов: ответ API, разложенный по подписям карточки.
/// </summary>
/// <remarks>
/// <para>
/// Строка несёт всё, что веб показывает в очереди, плюс имя мастера и первые строки текста:
/// на телефоне строка — единственное место, где отзыв видно до открытия карточки, а наведения
/// с подсказкой, как у веба, здесь нет. Полный текст остаётся в карточке.
/// </para>
/// <para>
/// Имя клиента сокращается так же, как в вебе (<c>formatShortName</c>): «Дмитрий К.».
/// Полное имя доступно администратору в карточке — строка списка не должна расти от длинного
/// имени и не должна читаться как досье в общественном месте.
/// </para>
/// <para>
/// Снимок строки неизменяем: после решения очередь перечитывается целиком, а не меняет
/// разобранный отзыв на месте. <see cref="ObservableObject"/> остаётся базой по общему
/// соглашению строк списков, чтобы вид строки можно было менять без пересборки коллекции.
/// </para>
/// </remarks>
public sealed partial class AdminReviewItem : ObservableObject
{
    /// <summary>
    /// Создаёт строку из ответа API.
    /// </summary>
    /// <param name="review">Отзыв из админской очереди.</param>
    /// <param name="status">Какая очередь показана: от неё зависит подпись ожидания.</param>
    /// <param name="now">Текущий момент: «ждёт 2 часа» не должен зависеть от часов устройства в тесте.</param>
    public AdminReviewItem(AdminReviewResponse review, AdminReviewStatusFilter status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(review);

        Id = review.Id;
        Rating = review.Rating;
        IsPublished = review.IsPublished;
        Stars = ReviewText.Stars(review.Rating);
        RatingText = ReviewText.Rating(review.Rating);
        StatusText = ReviewText.Status(review.IsPublished);
        CustomerName = ShortName(review.Customer.FullName);
        MasterName = review.Barber.DisplayName;
        MasterLine = Loc.T("Мастер: {0}", MasterName);
        TextPreview = Preview(review.Text);

        var timeZone = SalonClock.TryResolve(review.Visit.TimeZoneId);

        // Для опубликованного отзыва ожидание не считается: он не ждёт решения, и веб
        // подписывает строку датой публикации. Очередь модерации, наоборот, говорит,
        // сколько отзыв ждёт, — это и есть её главная подпись.
        WaitingText = status == AdminReviewStatusFilter.Pending
            ? AdminText.WaitingSince(review.CreatedAt, now)
            : Loc.T("опубликован {0}", ReviewText.ReviewDate(review.CreatedAt, timeZone));
    }

    /// <summary>
    /// Идентификатор отзыва — для перехода в карточку.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Оценка от 1 до 5.
    /// </summary>
    public int Rating { get; }

    /// <summary>
    /// Опубликован ли отзыв.
    /// </summary>
    public bool IsPublished { get; }

    /// <summary>
    /// Звёзды одной строкой: «★★★★☆».
    /// </summary>
    public string Stars { get; }

    /// <summary>
    /// Оценка числом: «5 / 5».
    /// </summary>
    public string RatingText { get; }

    /// <summary>
    /// Подпись статуса: «На модерации» или «Опубликован».
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Имя клиента, сокращённое как в вебе: «Дмитрий К.».
    /// </summary>
    public string CustomerName { get; }

    /// <summary>
    /// Имя мастера из ответа.
    /// </summary>
    public string MasterName { get; }

    /// <summary>
    /// Строка мастера: «Мастер: Иван».
    /// </summary>
    public string MasterLine { get; }

    /// <summary>
    /// Первые строки отзыва одной строкой; «Без комментария.» — оценки без текста.
    /// </summary>
    public string TextPreview { get; }

    /// <summary>
    /// Сколько отзыв ждёт решения либо когда он опубликован.
    /// </summary>
    public string WaitingText { get; }

    /// <summary>
    /// Сокращает имя клиента: первое слово и первая буква второго.
    /// </summary>
    /// <remarks>
    /// Повторяет <c>formatShortName</c> веба: пустое имя — «Клиент», одно слово остаётся
    /// целиком, у остальных вторая часть сокращается до инициала. Буква второго слова
    /// поднимается в верхний регистр в культуре языка: в английском инициал тоже заглавный.
    /// </remarks>
    private static string ShortName(string fullName)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length switch
        {
            0 => Loc.T("Клиент"),
            1 => words[0],
            _ => string.Concat(
                words[0],
                " ",
                char.ToUpper(words[1][0], Loc.Culture),
                "."),
        };
    }

    /// <summary>
    /// Склеивает текст отзыва в одну строку для строки списка.
    /// </summary>
    /// <remarks>
    /// Переводы строк и повторные пробелы схлопываются: строка показывает начало текста,
    /// а не его вёрстку. Пустой текст — та же подпись, что у списка «Мои отзывы».
    /// </remarks>
    private static string Preview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ReviewText.NoComment;
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(" ", words);
    }
}
