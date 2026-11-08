using System.Globalization;
using BarberSearch.Application.Admin;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Salons;
using Microsoft.EntityFrameworkCore;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Агрегатное чтение админской статистики из PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Это cross-aggregate read: статистика не принадлежит ни одному агрегату, поэтому
/// живёт отдельным репозиторием, как сводка. Только чтение — мутаций нет.
/// </para>
/// <para>
/// Сложные агрегаты (<c>date_trunc</c>, <c>percentile_cont</c>, <c>FILTER</c>)
/// выполняет база через параметризованный <c>SqlQuery</c>: склейки строк нет, значения
/// периода и статусов уходят параметрами. Простые счётчики тоже SQL — так один проход
/// считает пару «текущий/предыдущий период» без двух round-trip.
/// </para>
/// <para>
/// Все запросы последовательные: <c>DbContext</c> не потокобезопасен, поэтому
/// <c>Task.WhenAll</c> здесь запрещён. Ни один запрос не меняет схему и не требует
/// индекса сверх существующих: фильтры идут по <c>status</c>/<c>start_at</c>/
/// <c>created_at</c>, а 30-дневные агрегаты укладываются в бюджет (<c>EXPLAIN</c>
/// в <c>verification/</c> среза).
/// </para>
/// </remarks>
public sealed class AdminStatisticsRepository : IAdminStatisticsRepository
{
    private const int TOP_LIMIT = 10;
    private const int MIN_TOP_VISITS = 3;

    private const int PENDING_STATUS = (int)BookingStatus.Pending;
    private const int CONFIRMED_STATUS = (int)BookingStatus.Confirmed;
    private const int CANCELLED_STATUS = (int)BookingStatus.Cancelled;
    private const int COMPLETED_STATUS = (int)BookingStatus.Completed;
    private const int NO_SHOW_STATUS = (int)BookingStatus.NoShow;

    private const int REQUEST_PENDING = (int)BarberRequestStatus.Pending;
    private const int REQUEST_APPROVED = (int)BarberRequestStatus.Approved;
    private const int REQUEST_REJECTED = (int)BarberRequestStatus.Rejected;

    private const int SALON_PENDING = (int)SalonModerationStatus.Pending;
    private const int SALON_ACTIVE = (int)SalonModerationStatus.Active;
    private const int SALON_REJECTED = (int)SalonModerationStatus.Rejected;

    private readonly ApplicationDbContext _context;

    public AdminStatisticsRepository(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<AdminStatisticsSnapshot> ReadAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var overview = await ReadOverviewAsync(filters, cancellationToken);
        var series = await ReadSeriesAsync(filters, cancellationToken);
        var clients = await ReadClientsAsync(filters, cancellationToken);
        var supply = await ReadSupplyAsync(filters, cancellationToken);
        var salonsByKind = await ReadSalonsByKindAsync(cancellationToken);
        var quality = await ReadQualityAsync(filters, cancellationToken);
        var barbers = await ReadBarberTopAsync(filters, cancellationToken);
        var services = await ReadServiceTopAsync(filters, cancellationToken);
        var cities = await ReadCityTopAsync(filters, cancellationToken);

        return new AdminStatisticsSnapshot(
            overview.Counters,
            overview.Statuses,
            series,
            clients,
            supply,
            salonsByKind,
            quality,
            new AdminStatisticsTop(barbers, services, cities));
    }

    /// <summary>
    /// Обзор и статусы одним проходом: оба блока режут <c>bookings</c> по
    /// <c>status</c>/<c>start_at</c>, и второй запрос читал бы те же строки заново.
    /// Новые аккаунты — отдельная таблица, поэтому отдельный маленький запрос.
    /// </summary>
    private async Task<(AdminStatisticsCounters Counters, AdminStatisticsStatuses Statuses)>
        ReadOverviewAsync(
            AdminStatisticsFilters filters,
            CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;
        var previousFrom = filters.PreviousFromInclusive;
        var previousTo = filters.PreviousToExclusive;
        var now = filters.Now;

        var bookings = await _context.Database.SqlQuery<CountersRow>($"""
            SELECT
                count(*) FILTER (
                    WHERE status = {COMPLETED_STATUS}
                      AND start_at >= {from} AND start_at < {to})::int AS "CompletedVisits",
                COALESCE(sum(price) FILTER (
                    WHERE status = {COMPLETED_STATUS}
                      AND start_at >= {from} AND start_at < {to}), 0)::numeric AS "Turnover",
                count(*) FILTER (
                    WHERE status = {NO_SHOW_STATUS}
                      AND start_at >= {from} AND start_at < {to})::int AS "NoShowCount",
                count(*) FILTER (
                    WHERE status IN ({COMPLETED_STATUS}, {NO_SHOW_STATUS})
                      AND start_at >= {from} AND start_at < {to})::int AS "NoShowSample",
                count(*) FILTER (
                    WHERE status = {COMPLETED_STATUS}
                      AND start_at >= {previousFrom} AND start_at < {previousTo})::int AS "PreviousCompletedVisits",
                COALESCE(sum(price) FILTER (
                    WHERE status = {COMPLETED_STATUS}
                      AND start_at >= {previousFrom} AND start_at < {previousTo}), 0)::numeric AS "PreviousTurnover",
                count(*) FILTER (
                    WHERE status = {NO_SHOW_STATUS}
                      AND start_at >= {previousFrom} AND start_at < {previousTo})::int AS "PreviousNoShowCount",
                count(*) FILTER (
                    WHERE status IN ({COMPLETED_STATUS}, {NO_SHOW_STATUS})
                      AND start_at >= {previousFrom} AND start_at < {previousTo})::int AS "PreviousNoShowSample",
                count(*) FILTER (
                    WHERE status IN ({PENDING_STATUS}, {CONFIRMED_STATUS})
                      AND start_at < {now})::int AS "StuckBookings",
                count(*) FILTER (
                    WHERE status = {PENDING_STATUS}
                      AND start_at >= {from} AND start_at < {to})::int AS "Pending",
                count(*) FILTER (
                    WHERE status = {CONFIRMED_STATUS}
                      AND start_at >= {from} AND start_at < {to})::int AS "Confirmed",
                count(*) FILTER (
                    WHERE status = {COMPLETED_STATUS}
                      AND start_at >= {from} AND start_at < {to})::int AS "Completed",
                count(*) FILTER (
                    WHERE status = {CANCELLED_STATUS}
                      AND start_at >= {from} AND start_at < {to})::int AS "Cancelled",
                count(*) FILTER (
                    WHERE status = {NO_SHOW_STATUS}
                      AND start_at >= {from} AND start_at < {to})::int AS "NoShow",
                count(*) FILTER (
                    WHERE start_at >= {from} AND start_at < {to}
                      AND start_at < {now})::int AS "Due",
                count(*) FILTER (
                    WHERE start_at >= {from} AND start_at < {to}
                      AND start_at < {now}
                      AND status IN ({CANCELLED_STATUS}, {COMPLETED_STATUS}, {NO_SHOW_STATUS}))::int AS "Closed"
            FROM bookings
            """)
            .SingleAsync(cancellationToken);

        var accounts = await _context.Database.SqlQuery<AccountsRow>($"""
            SELECT
                count(*) FILTER (
                    WHERE created_at >= {from} AND created_at < {to})::int AS "NewAccounts",
                count(*) FILTER (
                    WHERE created_at >= {previousFrom}
                      AND created_at < {previousTo})::int AS "PreviousNewAccounts"
            FROM users
            """)
            .SingleAsync(cancellationToken);

        var counters = new AdminStatisticsCounters(
            bookings.CompletedVisits,
            bookings.Turnover,
            bookings.NoShowCount,
            bookings.NoShowSample,
            bookings.PreviousCompletedVisits,
            bookings.PreviousTurnover,
            bookings.PreviousNoShowCount,
            bookings.PreviousNoShowSample,
            bookings.StuckBookings,
            accounts.NewAccounts,
            accounts.PreviousNewAccounts);

        var statuses = new AdminStatisticsStatuses(
            bookings.Pending,
            bookings.Confirmed,
            bookings.Completed,
            bookings.Cancelled,
            bookings.NoShow,
            bookings.Due,
            bookings.Closed);

        return (counters, statuses);
    }

    /// <summary>
    /// Разреженный ряд: созданные записи по <c>created_at</c>, визиты — по <c>start_at</c>.
    /// </summary>
    /// <remarks>
    /// Бакет — день или ISO-неделя с понедельника. Плотность добавляет обработчик:
    /// <c>generate_series</c> здесь не нужен, потому что пустые бакеты всё равно
    /// нулевые, а список дат детерминирован.
    /// </remarks>
    private async Task<IReadOnlyList<AdminStatisticsSeriesBucket>> ReadSeriesAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;
        var unit = filters.Days <= 92 ? "day" : "week";

        var rows = await _context.Database.SqlQuery<SeriesRow>($"""
            SELECT
                date_trunc({unit}, point.at_utc)::date AS "Date",
                sum(point.created)::int AS "Created",
                sum(point.completed)::int AS "Completed",
                sum(point.cancelled)::int AS "Cancelled",
                sum(point.no_show)::int AS "NoShow"
            FROM (
                SELECT
                    (created_at AT TIME ZONE 'UTC') AS at_utc,
                    1 AS created,
                    0 AS completed,
                    0 AS cancelled,
                    0 AS no_show
                FROM bookings
                WHERE created_at >= {from} AND created_at < {to}

                UNION ALL

                SELECT
                    (start_at AT TIME ZONE 'UTC'),
                    0,
                    (status = {COMPLETED_STATUS})::int,
                    (status = {CANCELLED_STATUS})::int,
                    (status = {NO_SHOW_STATUS})::int
                FROM bookings
                WHERE start_at >= {from} AND start_at < {to}
            ) AS point
            GROUP BY 1
            """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AdminStatisticsSeriesBucket(
                row.Date,
                row.Created,
                row.Completed,
                row.Cancelled,
                row.NoShow))
            .ToList();
    }

    /// <summary>
    /// Клиенты периода: с визитом, вернувшиеся, первый визит, гости и отзывы.
    /// </summary>
    private async Task<AdminStatisticsClients> ReadClientsAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;

        var row = await _context.Database.SqlQuery<ClientsRow>($"""
            WITH completed AS (
                SELECT b.user_id
                FROM bookings b
                WHERE b.status = {COMPLETED_STATUS}
                  AND b.start_at >= {from} AND b.start_at < {to}
                  AND b.user_id IS NOT NULL
            ),
            visitors AS (
                SELECT DISTINCT user_id FROM completed
            )
            SELECT
                (SELECT count(*) FROM visitors)::int AS "WithVisit",
                (SELECT count(*) FROM visitors c
                 WHERE EXISTS (
                     SELECT 1 FROM bookings p
                     WHERE p.user_id = c.user_id
                       AND p.status = {COMPLETED_STATUS}
                       AND p.start_at < {from}))::int AS "Returning",
                (SELECT count(*) FROM visitors c
                 WHERE NOT EXISTS (
                     SELECT 1 FROM bookings p
                     WHERE p.user_id = c.user_id
                       AND p.status = {COMPLETED_STATUS}
                       AND p.start_at < {from}))::int AS "FirstVisit",
                (SELECT count(*) FROM bookings
                 WHERE user_id IS NULL
                   AND created_at >= {from} AND created_at < {to})::int AS "GuestBookings",
                (SELECT count(*) FROM bookings
                 WHERE created_at >= {from} AND created_at < {to})::int AS "CreatedBookings",
                (SELECT count(*) FROM reviews r
                 JOIN bookings b ON b.id = r.booking_id
                 WHERE b.status = {COMPLETED_STATUS}
                   AND b.start_at >= {from} AND b.start_at < {to})::int AS "Reviews"
            """)
            .SingleAsync(cancellationToken);

        return new AdminStatisticsClients(
            row.WithVisit,
            row.Returning,
            row.FirstVisit,
            row.GuestBookings,
            row.CreatedBookings,
            row.Reviews);
    }

    /// <summary>
    /// Предложение: активные и работавшие барберы, новые профили.
    /// </summary>
    private async Task<AdminStatisticsSupply> ReadSupplyAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;

        var row = await _context.Database.SqlQuery<SupplyRow>($"""
            SELECT
                (SELECT count(*) FROM barber_profiles
                 WHERE is_active AND is_verified)::int AS "ActiveBarbers",
                (SELECT count(DISTINCT b.barber_profile_id)
                 FROM bookings b
                 JOIN barber_profiles p ON p.id = b.barber_profile_id
                 WHERE b.status = {COMPLETED_STATUS}
                   AND b.start_at >= {from} AND b.start_at < {to}
                   AND p.is_active AND p.is_verified)::int AS "WorkingBarbers",
                (SELECT count(*) FROM barber_profiles
                 WHERE created_at >= {from} AND created_at < {to})::int AS "NewProfiles"
            """)
            .SingleAsync(cancellationToken);

        return new AdminStatisticsSupply(
            row.ActiveBarbers,
            row.WorkingBarbers,
            row.NewProfiles);
    }

    /// <summary>
    /// Салоны по типу места: все строки, включая выключенные и скрытые.
    /// </summary>
    private async Task<IReadOnlyList<AdminStatisticsSalonKind>> ReadSalonsByKindAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _context.Database.SqlQueryRaw<SalonKindRow>("""
            SELECT
                kind::int AS "Kind",
                count(*) FILTER (WHERE is_listed)::int AS "Listed",
                count(*) FILTER (WHERE NOT is_listed)::int AS "Unlisted"
            FROM salons
            GROUP BY kind
            ORDER BY kind
            """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AdminStatisticsSalonKind(
                FormatPlaceKind(row.Kind),
                row.Listed,
                row.Unlisted))
            .ToList();
    }

    /// <summary>
    /// Качество: опубликованные отзывы периода и модерация заявок и салонов.
    /// </summary>
    private async Task<AdminStatisticsQuality> ReadQualityAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;

        var reviews = await _context.Database.SqlQuery<ReviewsQualityRow>($"""
            SELECT
                avg(rating)::float8 AS "Rating",
                count(*) FILTER (WHERE is_published)::int AS "Reviews",
                count(*) FILTER (WHERE is_published AND rating = 1)::int AS "Rating1",
                count(*) FILTER (WHERE is_published AND rating = 2)::int AS "Rating2",
                count(*) FILTER (WHERE is_published AND rating = 3)::int AS "Rating3",
                count(*) FILTER (WHERE is_published AND rating = 4)::int AS "Rating4",
                count(*) FILTER (WHERE is_published AND rating = 5)::int AS "Rating5",
                count(*)::int AS "NewReviews"
            FROM reviews
            WHERE created_at >= {from} AND created_at < {to}
            """)
            .SingleAsync(cancellationToken);

        var requests = await _context.Database.SqlQuery<ModerationRow>($"""
            SELECT
                count(*) FILTER (
                    WHERE created_at >= {from} AND created_at < {to})::int AS "Submitted",
                count(*) FILTER (
                    WHERE status = {REQUEST_APPROVED}
                      AND reviewed_at >= {from} AND reviewed_at < {to})::int AS "Approved",
                count(*) FILTER (
                    WHERE status = {REQUEST_REJECTED}
                      AND reviewed_at >= {from} AND reviewed_at < {to})::int AS "Rejected",
                percentile_cont(0.5) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM (reviewed_at - created_at)) / 3600.0)
                    FILTER (WHERE status IN ({REQUEST_APPROVED}, {REQUEST_REJECTED})
                              AND reviewed_at >= {from} AND reviewed_at < {to}) AS "MedianDecisionHours"
            FROM barber_requests
            """)
            .SingleAsync(cancellationToken);

        var salons = await _context.Database.SqlQuery<ModerationRow>($"""
            SELECT
                count(*) FILTER (
                    WHERE created_at >= {from} AND created_at < {to}
                      AND (moderation_status = {SALON_PENDING}
                           OR reviewed_at IS NOT NULL))::int AS "Submitted",
                count(*) FILTER (
                    WHERE moderation_status = {SALON_ACTIVE}
                      AND reviewed_at >= {from} AND reviewed_at < {to})::int AS "Approved",
                count(*) FILTER (
                    WHERE moderation_status = {SALON_REJECTED}
                      AND reviewed_at >= {from} AND reviewed_at < {to})::int AS "Rejected",
                percentile_cont(0.5) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM (reviewed_at - created_at)) / 3600.0)
                    FILTER (WHERE moderation_status IN ({SALON_ACTIVE}, {SALON_REJECTED})
                              AND reviewed_at >= {from} AND reviewed_at < {to}) AS "MedianDecisionHours"
            FROM salons
            """)
            .SingleAsync(cancellationToken);

        return new AdminStatisticsQuality(
            reviews.Rating is { } rating ? (decimal)rating : null,
            reviews.Reviews,
            reviews.Rating1,
            reviews.Rating2,
            reviews.Rating3,
            reviews.Rating4,
            reviews.Rating5,
            reviews.NewReviews,
            ToModeration(requests),
            ToModeration(salons));
    }

    /// <summary>
    /// Топ барберов: <c>INNER JOIN</c> с профилем, поэтому удалённый профиль уходит
    /// в «Остальные» и сверка не ломается.
    /// </summary>
    private async Task<IReadOnlyList<AdminStatisticsBarberRow>> ReadBarberTopAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;

        var rows = await _context.Database.SqlQuery<BarberTopRow>($"""
            WITH stats AS (
                SELECT
                    barber_profile_id,
                    count(*) FILTER (WHERE status = {COMPLETED_STATUS})::int AS visits,
                    COALESCE(sum(price) FILTER (WHERE status = {COMPLETED_STATUS}), 0)::numeric AS turnover,
                    count(*) FILTER (WHERE status = {NO_SHOW_STATUS})::int AS no_show
                FROM bookings
                WHERE status IN ({COMPLETED_STATUS}, {NO_SHOW_STATUS})
                  AND start_at >= {from} AND start_at < {to}
                GROUP BY barber_profile_id
            )
            SELECT
                p.id AS "BarberProfileId",
                p.display_name AS "DisplayName",
                stats.visits AS "Visits",
                stats.turnover AS "Turnover",
                stats.no_show AS "NoShow",
                p.rating AS "Rating",
                p.reviews_count AS "ReviewsCount"
            FROM stats
            JOIN barber_profiles p ON p.id = stats.barber_profile_id
            WHERE stats.visits >= {MIN_TOP_VISITS}
            ORDER BY stats.visits DESC, stats.turnover DESC, p.id
            LIMIT {TOP_LIMIT}
            """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AdminStatisticsBarberRow(
                row.BarberProfileId,
                row.DisplayName,
                row.Visits,
                row.Turnover,
                row.NoShow,
                row.Rating,
                row.ReviewsCount))
            .ToList();
    }

    /// <summary>
    /// Топ услуг: группировка по услуге каталога, а не по строке барбера.
    /// </summary>
    private async Task<IReadOnlyList<AdminStatisticsServiceRow>> ReadServiceTopAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;

        var rows = await _context.Database.SqlQuery<ServiceTopRow>($"""
            SELECT
                h.id AS "HairServiceId",
                h.name AS "Name",
                count(*)::int AS "Visits",
                COALESCE(sum(b.price), 0)::numeric AS "Turnover"
            FROM bookings b
            JOIN barber_services bs ON bs.id = b.barber_service_id
            JOIN hair_services h ON h.id = bs.hair_service_id
            WHERE b.status = {COMPLETED_STATUS}
              AND b.start_at >= {from} AND b.start_at < {to}
            GROUP BY h.id, h.name
            HAVING count(*) >= {MIN_TOP_VISITS}
            ORDER BY "Visits" DESC, "Turnover" DESC, h.id
            LIMIT {TOP_LIMIT}
            """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AdminStatisticsServiceRow(
                row.HairServiceId,
                row.Name,
                row.Visits,
                row.Turnover))
            .ToList();
    }

    /// <summary>
    /// Топ городов по завершённым визитам с числом активных барберов города.
    /// </summary>
    /// <remarks>
    /// Город — из салона визита. Удалённый салон (его не удаляют, но строка может
    /// отсутствовать у старых данных) уходит в «Остальные».
    /// </remarks>
    private async Task<IReadOnlyList<AdminStatisticsCityRow>> ReadCityTopAsync(
        AdminStatisticsFilters filters,
        CancellationToken cancellationToken)
    {
        var from = filters.FromInclusive;
        var to = filters.ToExclusive;

        var rows = await _context.Database.SqlQuery<CityTopRow>($"""
            WITH city_stats AS (
                SELECT
                    s.city AS city,
                    count(*)::int AS visits,
                    COALESCE(sum(b.price), 0)::numeric AS turnover
                FROM bookings b
                JOIN salons s ON s.id = b.salon_id
                WHERE b.status = {COMPLETED_STATUS}
                  AND b.start_at >= {from} AND b.start_at < {to}
                GROUP BY s.city
            ),
            top_cities AS (
                SELECT city, visits, turnover
                FROM city_stats
                WHERE visits >= {MIN_TOP_VISITS}
                ORDER BY visits DESC, turnover DESC, city
                LIMIT {TOP_LIMIT}
            )
            SELECT
                t.city AS "Name",
                t.visits AS "Visits",
                t.turnover AS "Turnover",
                (SELECT count(DISTINCT bs.barber_profile_id)
                 FROM barber_salons bs
                 JOIN salons s2 ON s2.id = bs.salon_id
                 JOIN barber_profiles p ON p.id = bs.barber_profile_id
                 WHERE s2.city = t.city
                   AND p.is_active AND p.is_verified)::int AS "ActiveBarbers"
            FROM top_cities t
            ORDER BY t.visits DESC, t.turnover DESC, t.city
            """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AdminStatisticsCityRow(
                row.Name,
                row.Visits,
                row.Turnover,
                row.ActiveBarbers))
            .ToList();
    }

    private static AdminStatisticsModeration ToModeration(ModerationRow row)
        => new(row.Submitted, row.Approved, row.Rejected, row.MedianDecisionHours);

    /// <summary>
    /// Имя <c>PlaceKind</c> для контракта. Неизвестное число остаётся строкой,
    /// а не превращается в «Barbershop»: тип — данные, а не догадка.
    /// </summary>
    private static string FormatPlaceKind(int kind)
        => Enum.IsDefined((PlaceKind)kind)
            ? ((PlaceKind)kind).ToString()
            : kind.ToString(CultureInfo.InvariantCulture);

    private sealed class CountersRow
    {
        public int CompletedVisits { get; set; }
        public decimal Turnover { get; set; }
        public int NoShowCount { get; set; }
        public int NoShowSample { get; set; }
        public int PreviousCompletedVisits { get; set; }
        public decimal PreviousTurnover { get; set; }
        public int PreviousNoShowCount { get; set; }
        public int PreviousNoShowSample { get; set; }
        public int StuckBookings { get; set; }
        public int Pending { get; set; }
        public int Confirmed { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public int NoShow { get; set; }
        public int Due { get; set; }
        public int Closed { get; set; }
    }

    private sealed class AccountsRow
    {
        public int NewAccounts { get; set; }
        public int PreviousNewAccounts { get; set; }
    }

    private sealed class SeriesRow
    {
        public DateOnly Date { get; set; }
        public int Created { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public int NoShow { get; set; }
    }

    private sealed class ClientsRow
    {
        public int WithVisit { get; set; }
        public int Returning { get; set; }
        public int FirstVisit { get; set; }
        public int GuestBookings { get; set; }
        public int CreatedBookings { get; set; }
        public int Reviews { get; set; }
    }

    private sealed class SupplyRow
    {
        public int ActiveBarbers { get; set; }
        public int WorkingBarbers { get; set; }
        public int NewProfiles { get; set; }
    }

    private sealed class SalonKindRow
    {
        public int Kind { get; set; }
        public int Listed { get; set; }
        public int Unlisted { get; set; }
    }

    private sealed class ReviewsQualityRow
    {
        public double? Rating { get; set; }
        public int Reviews { get; set; }
        public int Rating1 { get; set; }
        public int Rating2 { get; set; }
        public int Rating3 { get; set; }
        public int Rating4 { get; set; }
        public int Rating5 { get; set; }
        public int NewReviews { get; set; }
    }

    private sealed class ModerationRow
    {
        public int Submitted { get; set; }
        public int Approved { get; set; }
        public int Rejected { get; set; }
        public double? MedianDecisionHours { get; set; }
    }

    private sealed class BarberTopRow
    {
        public Guid BarberProfileId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int Visits { get; set; }
        public decimal Turnover { get; set; }
        public int NoShow { get; set; }
        public decimal? Rating { get; set; }
        public int ReviewsCount { get; set; }
    }

    private sealed class ServiceTopRow
    {
        public Guid HairServiceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Visits { get; set; }
        public decimal Turnover { get; set; }
    }

    private sealed class CityTopRow
    {
        public string Name { get; set; } = string.Empty;
        public int Visits { get; set; }
        public decimal Turnover { get; set; }
        public int ActiveBarbers { get; set; }
    }
}
