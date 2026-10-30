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

    // Шаг сетки слотов, когда услуга не выбрана. При выбранной услуге шаг — её
    // BarberService.DurationMinutes (R-20): длительность читается из той же строки
    // barber_services, что и barberServiceId в ответе слотов.
    public const int DEFAULT_SLOT_DURATION_MINUTES = 30;

    // Текстовые поля
    public const int MAX_TEXT_LENGTH = 2000;

    // URL-поля (фото портфолио и миниатюры, сайт салона, аватар пользователя).
    // Предел совпадает с varchar(2048) в схеме: строка сверх него не пролезет в колонку,
    // поэтому домен обязан отклонить её раньше (R-24).
    public const int MAX_URL_LENGTH = 2048;

    // Email: RFC 5321 ограничивает полный адрес 254 символами (включая @ и домен).
    public const int MIN_EMAIL_LENGTH = 3;
    public const int MAX_EMAIL_LENGTH = 254;

    // Защита входа от перебора (R-21). Порог, окно сброса счётчика и экспоненциальная
    // задержка: после MAX_FAILED_LOGIN_ATTEMPTS-й неудачи — 1 минута, затем 2, 4, …,
    // но не больше LOGIN_LOCKOUT_MAX_MINUTES.
    public const int MAX_FAILED_LOGIN_ATTEMPTS = 5;
    public const int LOGIN_FAILURE_RESET_MINUTES = 15;
    public const int LOGIN_LOCKOUT_BASE_MINUTES = 1;
    public const int LOGIN_LOCKOUT_MAX_MINUTES = 60;

    // Расписание
    public const int DAYS_IN_WEEK = 7;

    // Бронирование
    public const int MAX_IDEMPOTENCY_KEY_LENGTH = 128;

    // Заметка барбера — свободный текст, который видит только барбер. Предел ограничивает
    // размер строки и защищает от случайной вставки целого документа в поле.
    public const int MAX_BARBER_NOTE_LENGTH = 1000;

    // Окно, в которое обязан попадать StartAt брони. Сравнение идёт по абсолютному
    // моменту (DateTimeOffset), поэтому часовой пояс салона на него не влияет; допуск
    // покрывает расхождение часов клиента и сервера, а не разницу поясов.
    public const int BOOKING_HORIZON_DAYS = 90;
    public const int BOOKING_START_TOLERANCE_MINUTES = 5;

    // Бронь обслуживает одну услугу, поэтому её длительность не может превышать верхнюю
    // границу длительности услуги (см. MAX_DURATION_MINUTES).
    public const int MAX_BOOKING_DURATION_MINUTES = 24 * 60;
}
