namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Админская статистика сервиса за период: обзор, записи, клиенты, предложение,
/// лидеры и качество.
/// </summary>
/// <remarks>
/// <para>
/// Контракт агрегатный и только для чтения: ни одного идентификатора клиента,
/// email или телефона в нём нет. Имена барберов, названия услуг и городов
/// публичны и остаются.
/// </para>
/// <para>
/// Период включителен по обеим датам и трактуется как UTC-сутки. Все доли — от
/// <c>0</c> до <c>1</c> либо <c>null</c>, когда знаменатель нулевой; неизвестное
/// значение не подменяется нулём. Деньги приходят числом и валютой <c>RUB</c>.
/// </para>
/// <para>
/// Секции типизированы намеренно: клиент не собирает KPI из массива и не угадывает
/// смысл поля по имени тега.
/// </para>
/// </remarks>
/// <param name="Period">Период, по которому посчитаны текущие значения.</param>
/// <param name="GeneratedAt">Момент расчёта в UTC.</param>
/// <param name="Overview">Шесть плиток обзора.</param>
/// <param name="Series">Плотный ряд записей и визитов по дням или неделям.</param>
/// <param name="Statuses">Статусы визитов и закрытие периода.</param>
/// <param name="Clients">Клиентская база периода.</param>
/// <param name="Supply">Предложение: барберы, профили и салоны.</param>
/// <param name="Top">Лидеры периода.</param>
/// <param name="Quality">Отзывы и модерация заявок и салонов.</param>
public record AdminStatisticsResponse(
    AdminStatisticsPeriodResponse Period,
    DateTimeOffset GeneratedAt,
    AdminStatisticsOverviewResponse Overview,
    AdminStatisticsSeriesResponse Series,
    AdminStatisticsStatusesResponse Statuses,
    AdminStatisticsClientsResponse Clients,
    AdminStatisticsSupplyResponse Supply,
    AdminStatisticsTopResponse Top,
    AdminStatisticsQualityResponse Quality);

/// <summary>
/// Период расчёта: включительные даты, длина в днях, пояс и предыдущий период.
/// </summary>
/// <param name="From">Первый день периода включительно (UTC).</param>
/// <param name="To">Последний день периода включительно (UTC).</param>
/// <param name="Days">Число дней от <see cref="From"/> до <see cref="To"/> включительно.</param>
/// <param name="TimeZone">Пояс расчёта; сейчас всегда <c>UTC</c>.</param>
/// <param name="Previous">Предыдущий период той же длины или <c>null</c> при <c>compare=false</c>.</param>
public record AdminStatisticsPeriodResponse(
    DateOnly From,
    DateOnly To,
    int Days,
    string TimeZone,
    AdminStatisticsPeriodRangeResponse? Previous);

/// <summary>
/// Границы предыдущего периода сравнения.
/// </summary>
/// <param name="From">Первый день предыдущего периода включительно.</param>
/// <param name="To">Последний день предыдущего периода включительно.</param>
public record AdminStatisticsPeriodRangeResponse(DateOnly From, DateOnly To);
