namespace BarberSearch.Application.Admin;

/// <summary>
/// Сырые агрегаты статистики до округления, долей и «Остальных».
/// </summary>
/// <remarks>
/// Репозиторий отдаёт то, что посчитала база, без бизнес-решений: нулевые знаменатели
/// остаются нулями, а <c>null</c> для доли появляется в обработчике. Так unit-тесты
/// проверяют округление и сравнение периодов без PostgreSQL, а SQL — только фильтры.
/// </remarks>
/// <param name="Counters">Обзор и новые аккаунты текущего и предыдущего периодов.</param>
/// <param name="Statuses">Статусы визитов периода и закрытие.</param>
/// <param name="Series">Разреженные бакеты ряда; плотность добавляет обработчик.</param>
/// <param name="Clients">Клиентские счётчики периода.</param>
/// <param name="Supply">Барберы, профили и салоны.</param>
/// <param name="SalonsByKind">Счётчики салонов по типу места.</param>
/// <param name="Quality">Отзывы и модерация.</param>
/// <param name="Top">Строки топов без хвоста «Остальные».</param>
public sealed record AdminStatisticsSnapshot(
    AdminStatisticsCounters Counters,
    AdminStatisticsStatuses Statuses,
    IReadOnlyList<AdminStatisticsSeriesBucket> Series,
    AdminStatisticsClients Clients,
    AdminStatisticsSupply Supply,
    IReadOnlyList<AdminStatisticsSalonKind> SalonsByKind,
    AdminStatisticsQuality Quality,
    AdminStatisticsTop Top);

/// <summary>
/// Счётчики обзора. Знаменатель неявок — завершённые плюс неявившиеся.
/// </summary>
/// <param name="CompletedVisits">Завершённые визиты периода.</param>
/// <param name="Turnover">Сумма цен завершённых визитов.</param>
/// <param name="NoShowCount">Неявки периода.</param>
/// <param name="NoShowSample">Завершённые плюс неявившиеся периода.</param>
/// <param name="PreviousCompletedVisits">Завершённые визиты предыдущего периода.</param>
/// <param name="PreviousTurnover">Оборот предыдущего периода.</param>
/// <param name="PreviousNoShowCount">Неявки предыдущего периода.</param>
/// <param name="PreviousNoShowSample">Знаменатель неявок предыдущего периода.</param>
/// <param name="StuckBookings">Снимок активных броней со стартом в прошлом.</param>
/// <param name="NewAccounts">Аккаунты, созданные в периоде.</param>
/// <param name="PreviousNewAccounts">Аккаунты предыдущего периода.</param>
public sealed record AdminStatisticsCounters(
    int CompletedVisits,
    decimal Turnover,
    int NoShowCount,
    int NoShowSample,
    int PreviousCompletedVisits,
    decimal PreviousTurnover,
    int PreviousNoShowCount,
    int PreviousNoShowSample,
    int StuckBookings,
    int NewAccounts,
    int PreviousNewAccounts);

/// <summary>Статусы визитов по <c>start_at</c> в периоде.</summary>
/// <param name="Pending">Ждут подтверждения.</param>
/// <param name="Confirmed">Подтверждены.</param>
/// <param name="Completed">Завершены.</param>
/// <param name="Cancelled">Отменены.</param>
/// <param name="NoShow">Неявки.</param>
/// <param name="Due">Уже начались на момент расчёта.</param>
/// <param name="Closed">Из начавшихся: завершены, отменены или неявка.</param>
public sealed record AdminStatisticsStatuses(
    int Pending,
    int Confirmed,
    int Completed,
    int Cancelled,
    int NoShow,
    int Due,
    int Closed);

/// <summary>
/// Разреженный бакет ряда: база возвращает только непустые дни или недели.
/// </summary>
/// <param name="Date">День или понедельник ISO-недели.</param>
/// <param name="Created">Созданные записи.</param>
/// <param name="Completed">Завершённые визиты.</param>
/// <param name="Cancelled">Отменённые визиты.</param>
/// <param name="NoShow">Неявившиеся визиты.</param>
public sealed record AdminStatisticsSeriesBucket(
    DateOnly Date,
    int Created,
    int Completed,
    int Cancelled,
    int NoShow);

/// <summary>Клиентские счётчики: уникальные клиенты и гостевые записи.</summary>
/// <param name="WithVisit">Уникальные клиенты с завершённым визитом.</param>
/// <param name="Returning">Из них с завершённым визитом до периода.</param>
/// <param name="FirstVisit">Первый завершённый визит попал в период.</param>
/// <param name="GuestBookings">Записи без аккаунта, созданные в периоде.</param>
/// <param name="CreatedBookings">Все записи, созданные в периоде.</param>
/// <param name="Reviews">Отзывы по завершённым визитам периода.</param>
public sealed record AdminStatisticsClients(
    int WithVisit,
    int Returning,
    int FirstVisit,
    int GuestBookings,
    int CreatedBookings,
    int Reviews);

/// <summary>Предложение периода.</summary>
/// <param name="ActiveBarbers">Активные и подтверждённые профили.</param>
/// <param name="WorkingBarbers">Из них с завершённым визитом в периоде.</param>
/// <param name="NewProfiles">Профили, созданные в периоде.</param>
public sealed record AdminStatisticsSupply(
    int ActiveBarbers,
    int WorkingBarbers,
    int NewProfiles);

/// <summary>Салоны по типу места.</summary>
/// <param name="Kind">Имя <c>PlaceKind</c>.</param>
/// <param name="Listed">Показаны в каталоге.</param>
/// <param name="Unlisted">Скрыты из каталога.</param>
public sealed record AdminStatisticsSalonKind(string Kind, int Listed, int Unlisted);

/// <summary>Отзывы и модерация периода.</summary>
/// <param name="Rating">Средняя оценка опубликованных отзывов или <c>null</c>.</param>
/// <param name="Reviews">Число опубликованных отзывов периода.</param>
/// <param name="Rating1">Оценок 1.</param>
/// <param name="Rating2">Оценок 2.</param>
/// <param name="Rating3">Оценок 3.</param>
/// <param name="Rating4">Оценок 4.</param>
/// <param name="Rating5">Оценок 5.</param>
/// <param name="NewReviews">Все отзывы, созданные в периоде.</param>
/// <param name="Requests">Модерация заявок «Стать барбером».</param>
/// <param name="Salons">Модерация салонов.</param>
public sealed record AdminStatisticsQuality(
    decimal? Rating,
    int Reviews,
    int Rating1,
    int Rating2,
    int Rating3,
    int Rating4,
    int Rating5,
    int NewReviews,
    AdminStatisticsModeration Requests,
    AdminStatisticsModeration Salons);

/// <summary>Счётчики модерации одного типа.</summary>
/// <param name="Submitted">Подано в периоде.</param>
/// <param name="Approved">Одобрено в периоде.</param>
/// <param name="Rejected">Отклонено в периоде.</param>
/// <param name="MedianDecisionHours">Медиана решения в часах или <c>null</c>.</param>
public sealed record AdminStatisticsModeration(
    int Submitted,
    int Approved,
    int Rejected,
    double? MedianDecisionHours);

/// <summary>Строки топов до расчёта «Остальных».</summary>
/// <param name="Barbers">Топ барберов.</param>
/// <param name="Services">Топ услуг.</param>
/// <param name="Cities">Топ городов.</param>
public sealed record AdminStatisticsTop(
    IReadOnlyList<AdminStatisticsBarberRow> Barbers,
    IReadOnlyList<AdminStatisticsServiceRow> Services,
    IReadOnlyList<AdminStatisticsCityRow> Cities);

/// <summary>Строка топа барберов.</summary>
/// <param name="BarberProfileId">Профиль.</param>
/// <param name="DisplayName">Публичное имя.</param>
/// <param name="Visits">Завершённые визиты.</param>
/// <param name="Turnover">Оборот.</param>
/// <param name="NoShow">Неявки.</param>
/// <param name="Rating">Рейтинг профиля на сейчас.</param>
/// <param name="ReviewsCount">Число отзывов профиля на сейчас.</param>
public sealed record AdminStatisticsBarberRow(
    Guid BarberProfileId,
    string DisplayName,
    int Visits,
    decimal Turnover,
    int NoShow,
    decimal? Rating,
    int ReviewsCount);

/// <summary>Строка топа услуг.</summary>
/// <param name="HairServiceId">Услуга каталога.</param>
/// <param name="Name">Название.</param>
/// <param name="Visits">Завершённые визиты.</param>
/// <param name="Turnover">Оборот.</param>
public sealed record AdminStatisticsServiceRow(
    Guid HairServiceId,
    string Name,
    int Visits,
    decimal Turnover);

/// <summary>Строка топа городов.</summary>
/// <param name="Name">Город.</param>
/// <param name="Visits">Завершённые визиты.</param>
/// <param name="Turnover">Оборот.</param>
/// <param name="ActiveBarbers">Активные и подтверждённые барберы города.</param>
public sealed record AdminStatisticsCityRow(
    string Name,
    int Visits,
    decimal Turnover,
    int ActiveBarbers);
