namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Лидеры периода: барберы, услуги и города.
/// </summary>
/// <remarks>
/// В каждой таблице только строки с минимум тремя завершёнными визитами.
/// «Остальные» — разница между итогом секции и суммой строк, поэтому удалённые
/// профили, услуги и салоны не ломают сверку.
/// </remarks>
/// <param name="Barbers">Барберы по завершённым визитам.</param>
/// <param name="Services">Услуги каталога по завершённым визитам.</param>
/// <param name="Cities">Города по завершённым визитам.</param>
public record AdminStatisticsTopResponse(
    AdminStatisticsBarberTopResponse Barbers,
    AdminStatisticsServiceTopResponse Services,
    AdminStatisticsCityTopResponse Cities);

/// <summary>
/// Хвост топа: что не попало в десять строк.
/// </summary>
/// <param name="Visits">Завершённые визиты.</param>
/// <param name="Turnover">Оборот по ним.</param>
public record AdminStatisticsTopOtherResponse(int Visits, decimal Turnover);

/// <summary>
/// Топ барберов с рейтингом профиля на сейчас.
/// </summary>
/// <param name="Rows">До десяти строк, отсортированных по визитам и обороту.</param>
/// <param name="Other">Всё, что не попало в строки.</param>
public record AdminStatisticsBarberTopResponse(
    IReadOnlyList<AdminStatisticsBarberTopRowResponse> Rows,
    AdminStatisticsTopOtherResponse Other);

/// <summary>
/// Строка топа барберов.
/// </summary>
/// <param name="BarberProfileId">Идентификатор профиля: публичный, не PII.</param>
/// <param name="DisplayName">Публичное имя барбера.</param>
/// <param name="Visits">Завершённые визиты периода.</param>
/// <param name="Turnover">Оборот по ним.</param>
/// <param name="AverageCheck">Средний чек или 0, если визитов нет (в топе их ≥ 3).</param>
/// <param name="NoShow">Неявки к этому барберу в периоде.</param>
/// <param name="Rating">Рейтинг профиля на сейчас или <c>null</c>.</param>
/// <param name="ReviewsCount">Число отзывов профиля на сейчас.</param>
public record AdminStatisticsBarberTopRowResponse(
    Guid BarberProfileId,
    string DisplayName,
    int Visits,
    decimal Turnover,
    decimal AverageCheck,
    int NoShow,
    decimal? Rating,
    int ReviewsCount);

/// <summary>
/// Топ услуг каталога.
/// </summary>
/// <param name="Rows">До десяти строк.</param>
/// <param name="Other">Всё, что не попало в строки.</param>
public record AdminStatisticsServiceTopResponse(
    IReadOnlyList<AdminStatisticsServiceTopRowResponse> Rows,
    AdminStatisticsTopOtherResponse Other);

/// <summary>
/// Строка топа услуг.
/// </summary>
/// <param name="HairServiceId">Идентификатор услуги каталога.</param>
/// <param name="Name">Название услуги.</param>
/// <param name="Visits">Завершённые визиты периода.</param>
/// <param name="Turnover">Оборот по ним.</param>
/// <param name="AverageCheck">Средний чек по услуге.</param>
public record AdminStatisticsServiceTopRowResponse(
    Guid HairServiceId,
    string Name,
    int Visits,
    decimal Turnover,
    decimal AverageCheck);

/// <summary>
/// Топ городов по завершённым визитам.
/// </summary>
/// <param name="Rows">До десяти строк.</param>
/// <param name="Other">Всё, что не попало в строки.</param>
public record AdminStatisticsCityTopResponse(
    IReadOnlyList<AdminStatisticsCityTopRowResponse> Rows,
    AdminStatisticsTopOtherResponse Other);

/// <summary>
/// Строка топа городов.
/// </summary>
/// <param name="Name">Город.</param>
/// <param name="Visits">Завершённые визиты периода.</param>
/// <param name="Turnover">Оборот по ним.</param>
/// <param name="AverageCheck">Средний чек по городу.</param>
/// <param name="ActiveBarbers">Активные и подтверждённые барберы города на сейчас.</param>
public record AdminStatisticsCityTopRowResponse(
    string Name,
    int Visits,
    decimal Turnover,
    decimal AverageCheck,
    int ActiveBarbers);
