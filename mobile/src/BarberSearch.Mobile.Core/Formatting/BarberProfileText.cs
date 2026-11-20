using System.Globalization;
using System.Net;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Notifications;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты кабинета мастера: карточка, прайс, уведомления и подписи модерации.
/// </summary>
/// <remarks>
/// <para>
/// Форматтеры собраны в одном месте, потому что одни и те же значения показывают разные
/// экраны: грейд — карточка и форма правки, цена — прайс и редактор услуги, время
/// уведомления — инбокс и колокольчик. Экран не собирает строки сам: иначе «Скрыта» и
/// «Скрыто» разошлись бы между разделами.
/// </para>
/// <para>
/// Тексты взяты из веба (<c>frontend/src/pages/barber-notifications-page.tsx</c>,
/// <c>frontend/src/lib/barber-profile-labels.ts</c>) и повторяют его правила: время
/// напоминания читается в поясе салона, а не телефона, и «Сегодня»/«Вчера» считаются
/// по календарным датам этого пояса. Неизвестный пояс не роняет строку — показывается
/// UTC, как и в остальном кабинете (<see cref="SalonClock"/>).
/// </para>
/// </remarks>
public static class BarberProfileText
{
    /// <summary>
    /// Порог бейджа: дальше число не растёт, чтобы кружок не расширялся.
    /// </summary>
    public const int UnreadBadgeCap = 99;

    /// <summary>
    /// Нет ответа: сеть, таймаут или сервер недоступен.
    /// </summary>
    public static string Network => Loc.T(
        "Нет связи с сервером. Проверьте подключение и повторите попытку.");

    /// <summary>
    /// Сервер просит подождать: ограничитель частоты.
    /// </summary>
    public static string TooManyRequests => Loc.T(
        "Слишком много запросов. Подождите минуту и повторите.");

    /// <summary>
    /// Сессии больше нет: сервер ответил <c>401</c>.
    /// </summary>
    public static string SessionExpired => Loc.T("Сеанс истёк. Войдите заново.");

    /// <summary>
    /// Русские подписи категорий услуг справочника.
    /// </summary>
    private static readonly Dictionary<ServiceCategory, string> Categories = new()
    {
        [ServiceCategory.Haircut] = "Стрижка",
        [ServiceCategory.Beard] = "Борода",
        [ServiceCategory.Coloring] = "Окрашивание",
        [ServiceCategory.Styling] = "Укладка",
        [ServiceCategory.HairTreatment] = "Уход",
        [ServiceCategory.Other] = "Другое",
    };

    /// <summary>
    /// Подписи состояния салона в списке «Мои салоны».
    /// </summary>
    private static readonly Dictionary<SalonModerationStatus, string> Moderations = new()
    {
        [SalonModerationStatus.Pending] = "На проверке",
        [SalonModerationStatus.Active] = "В каталоге",
        [SalonModerationStatus.Rejected] = "Отклонён",
    };

    /// <summary>
    /// Грейд: «Мастер», «Старший мастер», «Без грейда».
    /// </summary>
    /// <param name="grade">Грейд из карточки.</param>
    /// <returns>Русская подпись.</returns>
    public static string GradeText(Grade grade) => BarberText.GradeText(grade);

    /// <summary>
    /// Язык общения: «Русский», «English».
    /// </summary>
    /// <param name="language">Язык из карточки.</param>
    /// <returns>Самоназвание языка.</returns>
    public static string LanguageText(Language language) => BarberText.LanguageText(language);

    /// <summary>
    /// Способ оплаты: «Наличные», «СБП».
    /// </summary>
    /// <param name="method">Способ оплаты из карточки.</param>
    /// <returns>Русская подпись.</returns>
    public static string PaymentText(PaymentMethod method) => BarberText.PaymentText(method);

    /// <summary>
    /// Стаж в правильной форме: «8 лет», «1 год».
    /// </summary>
    /// <param name="years">Стаж в годах.</param>
    /// <returns>Число и форма слова.</returns>
    public static string ExperienceText(int years) => BarberText.Experience(years);

    /// <summary>
    /// Рейтинг с одним знаком после запятой; <c>null</c> — прочерк.
    /// </summary>
    /// <param name="rating">Средний рейтинг.</param>
    /// <returns>«4,8» или «—».</returns>
    public static string RatingText(decimal? rating) => BarberText.Rating(rating);

    /// <summary>
    /// Счётчик отзывов: «12 отзывов».
    /// </summary>
    /// <param name="count">Число опубликованных отзывов.</param>
    /// <returns>Счётчик в правильной форме.</returns>
    public static string ReviewsText(int count) => BarberText.Reviews(count);

    /// <summary>
    /// Подпись подтверждённого профиля: «Проверен».
    /// </summary>
    public static string VerifiedText => BarberText.Verified;

    /// <summary>
    /// Состояние салона: «На проверке», «В каталоге», «Отклонён».
    /// </summary>
    /// <param name="status">Состояние проверки салона.</param>
    /// <returns>Русская подпись.</returns>
    public static string ModerationText(SalonModerationStatus status) =>
        Loc.T(Moderations.TryGetValue(status, out var text) ? text : "На проверке");

    /// <summary>
    /// Категория услуги: «Стрижка», «Борода», «Уход».
    /// </summary>
    /// <param name="category">Категория справочника.</param>
    /// <returns>Русская подпись.</returns>
    public static string CategoryText(ServiceCategory category) =>
        Loc.T(Categories.TryGetValue(category, out var text) ? text : "Другое");

    /// <summary>
    /// Цена услуги: «2 400 ₽».
    /// </summary>
    /// <param name="price">Цена из прайса.</param>
    /// <returns>Строка цены.</returns>
    public static string PriceText(decimal price) => BarberText.Price(price);

    /// <summary>
    /// Длительность услуги: «45 мин», «1 ч 30 мин».
    /// </summary>
    /// <param name="minutes">Длительность в минутах.</param>
    /// <returns>Строка длительности.</returns>
    public static string DurationText(int minutes) => BarberText.Duration(minutes);

    /// <summary>
    /// Статус строки прайса: активная подписи не имеет, скрытая — «Скрыта».
    /// </summary>
    /// <param name="isActive">Можно ли записаться на услугу.</param>
    /// <returns>Пустая строка либо «Скрыта».</returns>
    public static string ServiceStatusText(bool isActive) =>
        isActive ? string.Empty : Loc.T("Скрыта");

    /// <summary>
    /// Подпись состояния фотографии портфолио в кабинете.
    /// </summary>
    /// <param name="photo">Фотография из собственной карточки.</param>
    /// <returns>«Скрыто администратором», «На проверке», «Опубликовано» и соседние.</returns>
    public static string PhotoStatusText(BarberSearch.Contracts.BarberProfiles.GetBarberProfile.BarberProfilePhotoResponse photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        if (photo.IsBlocked)
        {
            return Loc.T("Скрыто администратором");
        }

        return photo.ModerationStatus switch
        {
            PhotoModerationStatus.Rejected => Loc.T("Отклонено"),
            PhotoModerationStatus.Pending => Loc.T("На проверке"),
            _ => photo.IsPublished ? Loc.T("Опубликовано") : Loc.T("Скрыто"),
        };
    }

    /// <summary>
    /// Подпись фотографии либо «Без подписи».
    /// </summary>
    /// <param name="caption">Подпись владельца.</param>
    /// <returns>Текст подписи.</returns>
    public static string PhotoCaption(string? caption) =>
        string.IsNullOrWhiteSpace(caption) ? Loc.T("Без подписи") : caption.Trim();

    /// <summary>
    /// Заголовок уведомления.
    /// </summary>
    /// <remarks>
    /// Напоминание о визите называет срок в минутах — «Через 30 минут запись»; дайджест —
    /// число записей на день. Формы числительных повторяют веб, поэтому «1 запись» и
    /// «2 записи» не превращаются в «1 записей».
    /// </remarks>
    /// <param name="notification">Уведомление из инбокса.</param>
    /// <param name="leadMinutes">За сколько минут до визита приходит напоминание.</param>
    /// <returns>Заголовок строки.</returns>
    public static string NotificationTitle(
        BarberNotificationResponse notification,
        int leadMinutes)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Kind == BarberNotificationKind.Upcoming)
        {
            return Loc.T(
                "Через {0} запись",
                Loc.Plural(PluralKey.Minutes, Math.Max(leadMinutes, 0)));
        }

        return Loc.T(
            "Сегодня {0}",
            Bookings(Math.Max(notification.BookingsCount ?? 0, 0)));
    }

    /// <summary>
    /// Вторая строка уведомления: время, услуга и салон либо первая запись дня.
    /// </summary>
    /// <param name="notification">Уведомление из инбокса.</param>
    /// <returns>Подпись строки; пустая, если времени нет.</returns>
    public static string NotificationBody(BarberNotificationResponse notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var timeZone = SalonClock.TryResolve(notification.SalonTimeZoneId);

        if (notification.Kind == BarberNotificationKind.Digest)
        {
            return notification.StartAt is null
                ? string.Empty
                : Loc.T(
                    "Первая в {0}",
                    SalonClock.FormatTime(notification.StartAt.Value, timeZone));
        }

        var salon = string.IsNullOrWhiteSpace(notification.SalonName)
            ? null
            : string.Concat("«", notification.SalonName.Trim(), "»");

        var time = notification.StartAt is null
            ? null
            : SalonClock.FormatTime(notification.StartAt.Value, timeZone);

        return string.Join(
            " · ",
            new[] { time, notification.ServiceName, salon }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));
    }

    /// <summary>
    /// Время строки уведомления: «Сегодня, 14:02», «Вчера, 14:02», «15 ноября, 14:02».
    /// </summary>
    /// <remarks>
    /// «Сегодня» и «Вчера» — календарные дни в поясе салона: уведомление о визите в
    /// Екатеринбурге не должно становиться «вчерашним» из-за часов телефона в UTC.
    /// Более старые строки получают дату без года: инбокс хранит не так давно, а год
    /// в каждой строке только удлинял бы её.
    /// </remarks>
    /// <param name="createdAt">Когда уведомление создано.</param>
    /// <param name="now">Текущий момент — параметром, чтобы тест не ждал часов.</param>
    /// <param name="tz">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Дата и время строки.</returns>
    public static string NotificationTime(
        DateTimeOffset createdAt,
        DateTimeOffset now,
        TimeZoneInfo? tz)
    {
        var created = tz is null ? createdAt.ToUniversalTime() : SalonClock.ToLocal(createdAt, tz);
        var current = tz is null ? now.ToUniversalTime() : SalonClock.ToLocal(now, tz);

        var createdDay = DateOnly.FromDateTime(created.DateTime);
        var currentDay = DateOnly.FromDateTime(current.DateTime);
        var difference = currentDay.DayNumber - createdDay.DayNumber;

        var day = difference switch
        {
            0 => Loc.T("Сегодня"),
            1 => Loc.T("Вчера"),
            _ => SalonClock.FormatDayMonth(createdDay),
        };

        return string.Concat(day, ", ", SalonClock.FormatTime(createdAt, tz));
    }

    /// <summary>
    /// Число для бейджа: до 99 цифрами, дальше «99+».
    /// </summary>
    /// <param name="count">Число непрочитанных.</param>
    /// <returns>Подпись бейджа.</returns>
    public static string BadgeCount(int count) => count > UnreadBadgeCap
        ? string.Concat(
            UnreadBadgeCap.ToString(CultureInfo.InvariantCulture),
            "+")
        : Math.Max(count, 0).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Текст сбоя чтения по виду отказа и запасной строке экрана.
    /// </summary>
    /// <param name="exception">Исключение, пришедшее из API.</param>
    /// <param name="kind">Вид сбоя из <see cref="ApiFailureClassifier"/>.</param>
    /// <param name="fallback">Строка раздела, если причина не уточняется.</param>
    /// <returns>Текст для человека.</returns>
    public static string ReadError(
        Exception exception,
        ApiFailureKind kind,
        string fallback) =>
        kind == ApiFailureKind.Transport
            ? Network
            : exception is BarberSearchApiException apiException
                ? WriteError(apiException, fallback)
                : fallback;

    /// <summary>
    /// Текст сбоя записи по коду ответа и запасной строке экрана.
    /// </summary>
    /// <param name="exception">Исключение, пришедшее из API.</param>
    /// <param name="fallback">Строка раздела, если причина не уточняется.</param>
    /// <returns>Текст для человека.</returns>
    public static string WriteError(
        BarberSearchApiException exception,
        string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => TooManyRequests,
            HttpStatusCode.Unauthorized => SessionExpired,
            _ => fallback,
        };
    }

    /// <summary>
    /// Число записей в дайджесте с правильной формой слова.
    /// </summary>
    /// <remarks>
    /// <see cref="PluralKey"/> не содержит «записей»: ключ живёт в локализации, которую
    /// этот срез не меняет. Формы повторяют <c>tp('bookings')</c> веба, поэтому строка
    /// совпадает с браузером, а не подчиняется общему правилу «визитов».
    /// </remarks>
    private static string Bookings(int count)
    {
        if (Loc.Locale == Locale.En)
        {
            return string.Concat(
                count.ToString(CultureInfo.InvariantCulture),
                count == 1 ? " booking" : " bookings");
        }

        var rest = Math.Abs(count) % 100;
        var last = rest % 10;
        var word = rest is >= 11 and <= 14
            ? "записей"
            : last switch
            {
                1 => "запись",
                2 or 3 or 4 => "записи",
                _ => "записей",
            };

        return string.Concat(
            count.ToString(CultureInfo.InvariantCulture),
            " ",
            Loc.T(word));
    }
}
