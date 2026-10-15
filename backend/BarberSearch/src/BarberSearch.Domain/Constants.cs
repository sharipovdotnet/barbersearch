namespace BarberSearch.Domain;

public static class Constants
{
    // Портфолио
    public const int MAX_PHOTOS_COUNT = 10;

    // Рейтинг барбера хранится в той же шкале, что и Review.Rating: 1.00 .. 5.00.
    // Шкала 0..100 — это дело представления (фронта), в домене её нет.
    // null = отзывов ещё нет, Rating и ReviewsCount меняются только вместе.
    public const decimal MIN_RATING = 1m;
    public const decimal MAX_RATING = 5m;
    public const int NUMBER_DECIMAL_PLACES = 2;

    // Отзывы
    public const int MIN_REVIEWS_COUNT = 0;
    public const int MIN_REVIEW_RATING = 1;
    public const int MAX_REVIEW_RATING = 5;

    // Опыт работы
    public const int MIN_YEARS_OF_EXPERIENCE = 1;
    public const int MAX_YEARS_OF_EXPERIENCE = 70;

    // География
    public const double MIN_LATITUDE = -90d;
    public const double MAX_LATITUDE = 90d;
    public const double MIN_LONGITUDE = -180d;
    public const double MAX_LONGITUDE = 180d;

    // Услуги
    public const int MIN_DURATION_MINUTES = 1;
    public const int MAX_DURATION_MINUTES = 24 * 60;

    // Текстовые поля
    public const int MAX_TEXT_LENGTH = 2000;

    // Расписание
    public const int DAYS_IN_WEEK = 7;

    // Бронирование
    public const int MAX_IDEMPOTENCY_KEY_LENGTH = 128;
}
