namespace BarberSearch.Domain;

public static class Constants
{
    // Портфолио
    public const int MAX_PHOTOS_COUNT = 10;

    // Предел размера загружаемого файла фотографии (5 МиБ). Хранится в домене, а не в
    // конфигурации: второй источник правды разошёлся бы с проверкой, которую видит клиент.
    public const int MAX_PHOTO_BYTES = 5 * 1024 * 1024;

    // Префикс публичного URL загруженной фотографии. Один на домен, хранилище и контроллер
    // раздачи: строка собирается и разбирается только через PortfolioMediaUrl.
    public const string PORTFOLIO_MEDIA_PATH_PREFIX = "/api/media/portfolio";

    // Префикс публичного URL загруженного аватара. Один на домен, хранилище и контроллер
    // раздачи: строка собирается и разбирается только через UserAvatarMediaUrl.
    public const string AVATAR_MEDIA_PATH_PREFIX = "/api/media/avatars";

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

    // Горизонт проверки конфликтов при правке повторяющегося дня недели: от «сегодня»
    // в поясе салона. Дальше 60 дней расписание вручную не правят, а явная дата исключения
    // проверяется на всю глубину — дата известна, перебирать нечего.
    public const int SCHEDULE_CONFLICT_HORIZON_DAYS = 60;

    // Причина исключения на дату — короткая пометка, а не описание.
    public const int MAX_SCHEDULE_REASON_LENGTH = 500;

    // Бронирование
    public const int MAX_IDEMPOTENCY_KEY_LENGTH = 128;

    // Гость (walk-in) в брони: контактный снимок на момент визита, а не аккаунт.
    // Значения совпадают с varchar-колонками bookings: строка сверх предела не пролезет
    // в схему, поэтому домен обязан отклонить её раньше.
    public const int MAX_GUEST_FIRST_NAME_LENGTH = 100;
    public const int MAX_GUEST_LAST_NAME_LENGTH = 100;

    // Телефон гостя — свободная строка (номера бывают с пробелами, скобками и «+»),
    // предел ограничивает размер поля, а не формат.
    public const int MAX_GUEST_PHONE_LENGTH = 32;

    // Заметка барбера — свободный текст, который видит только барбер. Предел ограничивает
    // размер строки и защищает от случайной вставки целого документа в поле.
    public const int MAX_BARBER_NOTE_LENGTH = 1000;

    // Причина отмены, которую барбер обязан указать при отмене записи. Это короткая
    // пометка для клиента, а не описание: предел совпадает с причиной исключения расписания.
    public const int MAX_CANCELLATION_REASON_LENGTH = 500;

    // Заявка на роль барбера. Город и причина отказа — короткие тексты; «о себе» использует
    // общий предел MAX_TEXT_LENGTH (2000), как описание фото и текст отзыва.
    public const int MAX_BARBER_REQUEST_CITY_LENGTH = 100;
    public const int MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH = 500;

    // Причина отказа салону на модерации. Короткая пометка администратора; предел тот же,
    // что у причины отказа заявки в барберы, — это одинаковый по смыслу текст.
    public const int MAX_SALON_REJECTION_REASON_LENGTH = 500;

    // Причина отказа кадру на премодерации — одна на аватар и портфолио: это одинаковый
    // по смыслу текст администратора, и предел совпадает с varchar(500) в схеме.
    public const int MAX_PHOTO_REJECTION_REASON_LENGTH = 500;

    // Окно, в которое обязан попадать StartAt брони. Сравнение идёт по абсолютному
    // моменту (DateTimeOffset), поэтому часовой пояс салона на него не влияет; допуск
    // покрывает расхождение часов клиента и сервера, а не разницу поясов.
    public const int BOOKING_HORIZON_DAYS = 90;
    public const int BOOKING_START_TOLERANCE_MINUTES = 5;

    // Бронь обслуживает одну услугу, поэтому её длительность не может превышать верхнюю
    // границу длительности услуги (см. MAX_DURATION_MINUTES).
    public const int MAX_BOOKING_DURATION_MINUTES = 24 * 60;

    // Уведомления барберу. Снимки услуги и салона — короткие подписи для текста напоминания,
    // их пределы совпадают с varchar-колонками, поэтому домен обязан отклонить длинную строку
    // раньше, чем её отвергнет схема. Пояс — идентификатор IANA, а не произвольный текст.
    public const int MAX_NOTIFICATION_SERVICE_NAME_LENGTH = 200;
    public const int MAX_NOTIFICATION_SALON_NAME_LENGTH = 200;
    public const int MAX_NOTIFICATION_TIME_ZONE_ID_LENGTH = 100;

    // Текст последней ошибки доставки — диагностика без ПДн, а не сообщение пользователю.
    public const int MAX_NOTIFICATION_LAST_ERROR_LENGTH = 500;

    // Предел попыток доставки: на пятой неудаче уведомление окончательно Failed.
    public const int MAX_NOTIFICATION_DELIVERY_ATTEMPTS = 5;

    // Напоминание о визите создаётся за 15, 30 или 60 минут до начала — выбора «через 45 минут»
    // нет, поэтому проверяется принадлежность множеству, а не диапазону.
    public const int MIN_NOTIFICATION_LEAD_MINUTES = 15;
    public const int DEFAULT_NOTIFICATION_LEAD_MINUTES = 30;
    public const int MAX_NOTIFICATION_LEAD_MINUTES = 60;

    // Утренний дайджест: локальное время салона от 06:00 до 12:00 включительно, шаг 15 минут.
    // До первой стрижки остаётся время, а «после полудня» уже не утро.
    public const int MIN_NOTIFICATION_DIGEST_HOUR = 6;
    public const int MAX_NOTIFICATION_DIGEST_HOUR = 12;
    public const int NOTIFICATION_DIGEST_TIME_STEP_MINUTES = 15;

    // Окно срабатывания дайджеста: тик воркера раз в минуту обязан попасть в одну минуту,
    // поэтому окно ровно минута и сравнивается полуинтервалом [DigestTime, DigestTime + 1 мин).
    public const int NOTIFICATION_DIGEST_WINDOW_MINUTES = 1;

    // Ретенция инбокса: строки старше 180 дней удаляются суточной уборкой, чтобы таблица
    // не росла бесконечно. Те же значения — значения по умолчанию у RemindersOptions.
    public const int NOTIFICATION_RETENTION_DAYS = 180;

    // Размер пачки доставки по умолчанию: один тик не должен держать транзакцию на всю очередь.
    public const int NOTIFICATION_BATCH_SIZE = 100;

    // Срок жизни кода привязки Telegram и длина самого кода (base32, 32 символа).
    public const int TELEGRAM_LINK_CODE_LIFETIME_MINUTES = 10;
    public const int TELEGRAM_LINK_CODE_LENGTH = 32;
}
