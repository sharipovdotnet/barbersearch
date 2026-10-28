using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.Admin;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Common;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="BarberProfile"/>.
/// </summary>
public sealed class BarberProfileRepository : RepositoryBase<BarberProfile>, IBarberProfileRepository
{
    public BarberProfileRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public Task<Result<BarberProfile, Failure>> GetRootByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(Context.BarberProfiles, id, cancellationToken);

    public Task<Result<BarberProfile, Failure>> GetWithSalonsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(
            Context.BarberProfiles.Include(x => x.Salons),
            id,
            cancellationToken);

    public Task<Result<BarberProfile, Failure>> GetWithSalonsAndSchedulesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(
            Context.BarberProfiles
                .Include(x => x.Salons)
                    .ThenInclude(salon => salon.WorkingSchedules),
            id,
            cancellationToken);

    public Task<Result<BarberProfile, Failure>> GetWithServicesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(
            Context.BarberProfiles.Include(x => x.Services),
            id,
            cancellationToken);

    public Task<Result<BarberProfile, Failure>> GetWithPhotosAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(
            Context.BarberProfiles.Include(x => x.PortfolioPhotos),
            id,
            cancellationToken);

    public Task<Result<BarberProfile, Failure>> GetWithSpecializationsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(
            Context.BarberProfiles.Include(x => x.BarberSpecializations),
            id,
            cancellationToken);

    public Task<Result<BarberProfile, Failure>> GetWithTimeOffsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(
            Context.BarberProfiles.Include(x => x.TimeOffs),
            id,
            cancellationToken);

    public async Task<Result<Guid, Failure>> GetOwnerIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = await Context.BarberProfiles
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerId is null)
        {
            return NotFound(ApiErrorCodes.BarberProfile.NotFound, nameof(BarberProfile), id);
        }

        return ownerId.Value;
    }

    public async Task<Result<BarberSalon, Failure>> GetSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        // Узкое чтение одного салона вместе с расписанием: профиль, его услуги, фото,
        // специализации и отпуска для FindBarberProfileSalon не нужны.
        var salon = await Context.BarberSalons
            .AsNoTracking()
            .Include(x => x.WorkingSchedules)
            .FirstOrDefaultAsync(
                x => x.BarberProfileId == barberProfileId && x.SalonId == salonId,
                cancellationToken);

        if (salon is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.BarberProfile.FindSalonNotFound,
                DomainMessages.BARBER_NOT_IN_SALON,
                salonId));
        }

        return salon;
    }

    public async Task<Result<BarberProfile, Failure>> GetForRatingUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Узкое чтение корня: пять коллекций для UpdateRating не нужны, а row lock
        // сериализует конкурентные пересчёты одного профиля (AGENT.md §12.2).
        // Сырой SQL намеренно не композируется с LINQ: FOR UPDATE должен остаться
        // в том же запросе, что и чтение строки.
        //
        // xmin указывается явно: PostgreSQL не включает системные колонки в SELECT *,
        // а EF Core ждёт xmin в проекции — иначе токен конкурентности (R-05) не
        // материализуется, и UPDATE уходит с xmin = 0.
        var profiles = await Context.BarberProfiles
            .FromSqlInterpolated($"SELECT *, xmin FROM barber_profiles WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);

        var profile = profiles.SingleOrDefault();

        if (profile is null)
        {
            return NotFound(ApiErrorCodes.BarberProfile.NotFound, nameof(BarberProfile), id);
        }

        return profile;
    }

    public async Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids.Distinct().ToArray();

        if (idList.Length == 0)
        {
            return Array.Empty<BarberProfile>();
        }

        // Только корни, без дочерних коллекций: это путь поиска и расчёта слотов,
        // где Include пяти коллекций дал бы декартов взрыв (AGENT.md §15.6).
        var profiles = await Context.BarberProfiles
            .AsNoTracking()
            .Where(x => idList.Contains(x.Id))
            .ToListAsync(cancellationToken);

        return profiles;
    }

    /// <summary>
    /// Страница профилей: сначала с рейтингом, затем «без отзывов». Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Глубина offset-выдачи ограничена: <c>page * pageSize</c> не может превышать
    /// <see cref="MAX_PAGE_OFFSET"/> (10 000), иначе <c>400</c> с кодом
    /// <c>barberProfile.paging.depth</c> — до обращения к базе.
    /// </remarks>
    public async Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.BarberProfile.PagingPageInvalid,
                ApiErrorCodes.BarberProfile.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        if (ValidatePageOffset(
                page,
                pageSize,
                ApiErrorCodes.BarberProfile.PagingDepthInvalid) is { } depthError)
        {
            return depthError;
        }

        var query = Context.BarberProfiles.AsNoTracking();

        if (onlyActive)
        {
            query = query.Where(x => x.IsActive);
        }

        var profiles = await query
            // В PostgreSQL ORDER BY ... DESC ставит NULL первыми, поэтому «профили без отзывов»
            // отодвигаются явным ключом (rating IS NULL): сначала с рейтингом, потом остальные.
            .OrderBy(x => x.Rating == null)
            .ThenByDescending(x => x.Rating)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return profiles;
    }

    public async Task<Result<BarberService, Failure>> GetServiceAsync(
        Guid barberProfileId,
        Guid barberServiceId,
        CancellationToken cancellationToken = default)
    {
        // Узкое чтение строки barber_services: весь агрегат с пятью коллекциями
        // для проверки цены и активности не нужен (AGENT.md §15.6).
        var service = await Context.BarberServices
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == barberServiceId && x.BarberProfileId == barberProfileId,
                cancellationToken);

        if (service is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.BarberProfile.ServiceNotFound,
                $"""
                 Barber profile '{barberProfileId}' does not provide service
                 '{barberServiceId}'.
                 """,
                barberServiceId));
        }

        return service;
    }

    public async Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.BarberProfiles
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        return exists;
    }

    public async Task<Result<bool, Failure>> HasSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        var worksAtSalon = await Context.BarberSalons
            .AsNoTracking()
            .AnyAsync(
                link => link.BarberProfileId == barberProfileId && link.SalonId == salonId,
                cancellationToken);

        return worksAtSalon;
    }

    public async Task<Result<IReadOnlyList<BarberSearchItemResponse>, Failure>> SearchAsync(
        BarberSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Common.BarberSearchLimitInvalid,
                "Search limit must be positive.",
                nameof(filter.Limit)));
        }

        // Только публичная выдача: активный и подтверждённый профиль.
        var query = Context.BarberProfiles
            .AsNoTracking()
            .Where(x => x.IsActive && x.IsVerified);

        if (!string.IsNullOrWhiteSpace(filter.City))
        {
            var city = filter.City.Trim();

            // Город — свойство салона, а не профиля. EXISTS по связи (барбер, салон)
            // не размножает строки профиля, в отличие от JOIN.
            query = query.Where(profile => Context.BarberSalons.Any(link =>
                link.BarberProfileId == profile.Id
                && Context.Salons.Any(salon =>
                    salon.Id == link.SalonId
                    && salon.IsActive
                    && salon.City == city)));
        }

        if (filter.SpecializationId is { } specializationId)
        {
            query = query.Where(profile => Context.BarberSpecializations.Any(link =>
                link.BarberProfileId == profile.Id
                && link.SpecializationId == specializationId));
        }

        if (filter.HairServiceId is { } hairServiceId)
        {
            query = query.Where(profile => Context.BarberServices.Any(service =>
                service.BarberProfileId == profile.Id
                && service.HairServiceId == hairServiceId
                && service.IsActive));
        }

        if (filter.Date is { } date)
        {
            var dayOfWeek = date.DayOfWeek;

            // Дата — это «есть рабочий день в этот день недели хотя бы в одном активном салоне».
            // Конкретные свободные интервалы с учётом таймзоны и отпусков считает /slots.
            query = query.Where(profile => Context.BarberSalons.Any(link =>
                link.BarberProfileId == profile.Id
                && Context.Salons.Any(salon => salon.Id == link.SalonId && salon.IsActive)
                && Context.WorkingSchedules.Any(schedule =>
                    schedule.BarberSalonId == link.Id
                    && schedule.DayOfWeek == dayOfWeek
                    && !schedule.IsDayOff)));
        }

        if (filter.Cursor is { } cursor)
        {
            if (cursor.Rating is null)
            {
                // null-рейтинг — последняя группа: продолжаем внутри неё по возрастанию id.
                query = query.Where(profile =>
                    profile.Rating == null
                    && profile.Id.CompareTo(cursor.Id) > 0);
            }
            else
            {
                var rating = cursor.Rating.Value;

                // Барберы с рейтингом: строго меньший рейтинг, либо тот же рейтинг
                // и больший id. null-рейтинг идёт после всех ненулевых.
                query = query.Where(profile =>
                    profile.Rating == null
                    || profile.Rating < rating
                    || (profile.Rating == rating && profile.Id.CompareTo(cursor.Id) > 0));
            }
        }

        var rows = await query
            // PostgreSQL по умолчанию ставит NULL первыми при DESC, поэтому порядок
            // задан тремя ключами: сначала «есть ли рейтинг», затем рейтинг, затем id.
            // Этот порядок терм в терм совпадает с выражением индекса
            // ix_barber_profiles_search_keyset (((rating IS NULL)), rating DESC, id)
            // WHERE is_active AND is_verified, поэтому планировщик идёт по индексу и
            // останавливается после Limit, не сортируя все видимые профили.
            .OrderBy(profile => profile.Rating == null)
            .ThenByDescending(profile => profile.Rating)
            .ThenBy(profile => profile.Id)
            .Take(filter.Limit)
            .Select(profile => new BarberSearchItemResponse(
                profile.Id,
                profile.DisplayName.Value,
                profile.Grade,
                profile.YearsOfExperience.Value,
                profile.Rating,
                profile.ReviewsCount,
                profile.IsVerified,
                null,
                null,
                null))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return rows;
        }

        var profileIds = rows.Select(row => row.Id).ToArray();

        // Название и город основного салона — второй запрос на уже урезанный набор id.
        // Так основной запрос остаётся простым EXISTS-запросом без JOIN коллекций.
        var salons = await (
            from link in Context.BarberSalons.AsNoTracking()
            join salon in Context.Salons.AsNoTracking() on link.SalonId equals salon.Id
            where profileIds.Contains(link.BarberProfileId) && salon.IsActive
            orderby link.IsPrimary descending, link.Id
            select new
            {
                link.BarberProfileId,
                link.SalonId,
                salon.Name,
                salon.City
            }).ToListAsync(cancellationToken);

        var primarySalons = salons
            .GroupBy(salon => salon.BarberProfileId)
            .ToDictionary(group => group.Key, group => group.First());

        return rows
            .Select(row => primarySalons.TryGetValue(row.Id, out var salon)
                ? row with
                {
                    PrimarySalonId = salon.SalonId,
                    PrimarySalonName = salon.Name,
                    City = salon.City
                }
                : row)
            .ToList();
    }

    public async Task<Result<IReadOnlyList<AdminBarberResponse>, Failure>> GetAdminPageAsync(
        AdminBarberStatus status,
        string? search,
        AdminBarberCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminBarber.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildAdminBarbersQuery(Context, status, search, cursor, limit)
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<AdminBarberDetailResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await BuildAdminBarberDetailQuery(Context, id)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.BarberProfile.NotFound,
                $"Barber profile '{id}' was not found.",
                id));
        }

        return ToAdminDetailResponse(row);
    }

    /// <summary>
    /// Админская карточка барбера: профиль, владелец и счётчики готовности.
    /// </summary>
    /// <remarks>
    /// Коллекции агрегата не загружаются: услуги, салоны, фото и рабочие дни считаются
    /// подзапросами <c>COUNT</c>, поэтому карточка остаётся одним SQL-запросом.
    /// Рабочие дни схлопываются в дни недели: чек-листу нужен ответ «сколько разных дней
    /// расписано», а не число строк расписания по всем салонам.
    /// </remarks>
    internal static IQueryable<AdminBarberDetailRow> BuildAdminBarberDetailQuery(
        ApplicationDbContext context,
        Guid id)
    {
        ArgumentNullException.ThrowIfNull(context);

        return from profile in context.BarberProfiles.AsNoTracking()
               join user in context.Users.AsNoTracking() on profile.UserId equals user.Id
               where profile.Id == id
               select new AdminBarberDetailRow(
                   profile.Id,
                   profile.UserId,
                   profile.DisplayName.Value,
                   user.Email,
                   user.PhoneNumber,
                   user.AvatarUrl,
                   profile.Grade,
                   profile.Bio,
                   profile.YearsOfExperience.Value,
                   profile.Rating,
                   profile.ReviewsCount,
                   profile.IsActive,
                   profile.IsVerified,
                   user.IsEmailVerified,
                   context.BarberServices.Count(
                       service => service.BarberProfileId == profile.Id),
                   context.BarberSalons.Count(
                       link => link.BarberProfileId == profile.Id),
                   context.WorkingSchedules
                       .Where(schedule => !schedule.IsDayOff
                           && context.BarberSalons.Any(link =>
                               link.Id == schedule.BarberSalonId
                               && link.BarberProfileId == profile.Id))
                       .Select(schedule => schedule.DayOfWeek)
                       .Distinct()
                       .Count(),
                   context.PortfolioPhotos.Count(
                       photo => photo.BarberProfileId == profile.Id),
                   profile.CreatedAt,
                   profile.UpdatedAt);
    }

    private static AdminBarberDetailResponse ToAdminDetailResponse(AdminBarberDetailRow row)
        => new(
            row.Id,
            row.UserId,
            row.DisplayName,
            row.Email,
            row.PhoneNumber,
            row.AvatarUrl,
            row.Grade,
            row.Bio,
            row.YearsOfExperience,
            row.Rating,
            row.ReviewsCount,
            row.IsActive,
            row.IsVerified,
            new AdminBarberReadinessResponse(
                IsProfileFilled: !string.IsNullOrWhiteSpace(row.Bio)
                    && row.YearsOfExperience > 0,
                IsEmailVerified: row.IsEmailVerified,
                ServicesCount: row.ServicesCount,
                SalonsCount: row.SalonsCount,
                ScheduleDaysCount: row.ScheduleDaysCount,
                PhotosCount: row.PhotosCount),
            row.CreatedAt,
            row.UpdatedAt);

    /// <summary>
    /// Админская выборка барберов: профиль, владелец, поиск и курсор.
    /// </summary>
    /// <remarks>
    /// Отдельный <c>join</c> по пользователю нужен для email: навигации между агрегатами
    /// в домене нет. Поиск идёт по имени профиля и email владельца через <c>ILIKE</c>:
    /// регистр не важен, служебные символы шаблона экранируются, чтобы «%» в запросе
    /// не превращался в «найти всё». Направление сортировки задаёт статус.
    /// </remarks>
    internal static IQueryable<AdminBarberResponse> BuildAdminBarbersQuery(
        ApplicationDbContext context,
        AdminBarberStatus status = AdminBarberStatus.All,
        string? search = null,
        AdminBarberCursor? cursor = null,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var users = context.Users
            .AsNoTracking()
            .Select(user => new
            {
                user.Id,
                user.Email
            });

        var joined = from profile in context.BarberProfiles.AsNoTracking()
                     join user in users on profile.UserId equals user.Id
                     select new
                     {
                         profile,
                         user
                     };

        switch (status)
        {
            case AdminBarberStatus.Pending:
                joined = joined.Where(row => !row.profile.IsVerified);
                break;
            case AdminBarberStatus.Hidden:
                joined = joined.Where(row => !row.profile.IsActive);
                break;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = BuildSearchPattern(search);

            joined = joined.Where(row =>
                EF.Functions.ILike(row.profile.DisplayName.Value, pattern, "\\")
                || EF.Functions.ILike(row.user.Email, pattern, "\\"));
        }

        // Очередь проверки — FIFO: профиль, который ждёт дольше, стоит выше. Остальные
        // списки — свежими сверху, как админский список опубликованных отзывов.
        var descending = status != AdminBarberStatus.Pending;

        if (cursor is { } position)
        {
            joined = descending
                ? joined.Where(row =>
                    row.profile.CreatedAt < position.CreatedAt
                    || (row.profile.CreatedAt == position.CreatedAt
                        && row.profile.Id.CompareTo(position.Id) > 0))
                : joined.Where(row =>
                    row.profile.CreatedAt > position.CreatedAt
                    || (row.profile.CreatedAt == position.CreatedAt
                        && row.profile.Id.CompareTo(position.Id) > 0));
        }

        var ordered = descending
            ? joined
                .OrderByDescending(row => row.profile.CreatedAt)
                .ThenBy(row => row.profile.Id)
            : joined
                .OrderBy(row => row.profile.CreatedAt)
                .ThenBy(row => row.profile.Id);

        var paged = limit is { } take ? ordered.Take(take) : ordered;

        return paged.Select(row => new AdminBarberResponse(
            row.profile.Id,
            row.profile.DisplayName.Value,
            row.user.Email,
            row.profile.Grade,
            row.profile.Rating,
            row.profile.ReviewsCount,
            row.profile.IsActive,
            row.profile.IsVerified,
            row.profile.CreatedAt,
            row.profile.UpdatedAt));
    }

    /// <summary>
    /// Шаблон <c>ILIKE</c> для поиска по имени и email.
    /// </summary>
    /// <remarks>
    /// <c>%</c>, <c>_</c> и <c>\</c> из пользовательского запроса экранируются: иначе поиск
    /// «100%» находил бы всё, а «_» работал бы как любой одиночный символ.
    /// </remarks>
    private static string BuildSearchPattern(string search)
        => "%" + search
            .Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
        + "%";

    public async Task<Result<BarberSlotContext, Failure>> GetSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? hairServiceId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var isVisible = await Context.BarberProfiles
            .AsNoTracking()
            .AnyAsync(
                profile => profile.Id == barberProfileId
                           && profile.IsActive
                           && profile.IsVerified,
                cancellationToken);

        if (!isVisible)
        {
            return NotFound(
                ApiErrorCodes.BarberProfile.SlotsNotFound,
                nameof(BarberProfile),
                barberProfileId);
        }

        // Салон выбирается среди активных: неактивный салон не должен давать слоты.
        var salonLinks = await (
            from link in Context.BarberSalons.AsNoTracking()
            join salon in Context.Salons.AsNoTracking() on link.SalonId equals salon.Id
            where link.BarberProfileId == barberProfileId && salon.IsActive
            select new
            {
                link.Id,
                link.SalonId,
                link.IsPrimary,
                salon.TimeZoneId
            }).ToListAsync(cancellationToken);

        if (salonLinks.Count == 0)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Common.BarberSlotsSalonNotFound,
                $"The barber profile '{barberProfileId}' has no active salon.",
                barberProfileId));
        }

        var salonLink = salonId is { } requestedSalonId
            ? salonLinks.FirstOrDefault(link => link.SalonId == requestedSalonId)
            : salonLinks
                .OrderByDescending(link => link.IsPrimary)
                .ThenBy(link => link.SalonId)
                .FirstOrDefault();

        if (salonLink is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Common.BarberSlotsSalonNotFound,
                $"The barber profile '{barberProfileId}' does not work in salon '{salonId}'.",
                salonId ?? barberProfileId));
        }

        var schedule = await Context.WorkingSchedules
            .AsNoTracking()
            .Where(entry => entry.BarberSalonId == salonLink.Id
                            && entry.DayOfWeek == date.DayOfWeek
                            && !entry.IsDayOff)
            .Select(entry => new
            {
                entry.StartTime,
                entry.EndTime
            })
            .FirstOrDefaultAsync(cancellationToken);

        var durationMinutes = Constants.DEFAULT_SLOT_DURATION_MINUTES;
        Guid? barberServiceId = null;
        Guid? requestedHairServiceId = null;

        if (hairServiceId is { } hairServiceFilter)
        {
            // Длительность и идентификатор пары читаются одной строкой: в ответ слотов
            // уходит именно та barber_services, по которой построена сетка (R-20).
            var service = await BuildSlotServiceQuery(
                    Context,
                    barberProfileId,
                    hairServiceFilter)
                .FirstOrDefaultAsync(cancellationToken);

            if (service is null)
            {
                return new Failure(Error.NotFound(
                    ApiErrorCodes.Common.BarberSlotsServiceNotFound,
                    $"The barber profile '{barberProfileId}' does not provide hair service '{hairServiceFilter}'.",
                    hairServiceFilter));
            }

            durationMinutes = service.DurationMinutes;
            barberServiceId = service.BarberServiceId;
            requestedHairServiceId = service.HairServiceId;
        }

        // Окно шире любых реальных смещений UTC (−12..+14): обработчик затем отсекает
        // отпуска по точным границам локального дня.
        var dateStartUtc = new DateTimeOffset(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var windowStart = dateStartUtc.AddDays(-1);
        var windowEnd = dateStartUtc.AddDays(2);

        var timeOffs = await Context.TimeOffs
            .AsNoTracking()
            .Where(timeOff => timeOff.BarberProfileId == barberProfileId
                              && timeOff.StartAt < windowEnd
                              && timeOff.EndAt > windowStart)
            .Select(timeOff => new TimeOffInterval(timeOff.StartAt, timeOff.EndAt))
            .ToListAsync(cancellationToken);

        return new BarberSlotContext(
            barberProfileId,
            salonLink.SalonId,
            salonLink.TimeZoneId,
            date,
            schedule is not null,
            schedule?.StartTime ?? default,
            schedule?.EndTime ?? default,
            durationMinutes,
            barberServiceId,
            requestedHairServiceId,
            timeOffs);
    }

    /// <summary>
    /// Строит чтение пары «услуга барбера» по услуге справочника.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>hairServiceId</c> — это <c>hair_services.id</c>, а возвращаемый
    /// <see cref="BarberSlotServiceLookup.BarberServiceId"/> — <c>barber_services.id</c>,
    /// которую принимает создание брони. Оба идентификатора и длительность проецируются
    /// из одной строки, поэтому в ответ слотов уходит та пара, по которой реально
    /// считалась сетка (R-20).
    /// </para>
    /// <para>
    /// У профиля не больше одной строки на услугу справочника: это гарантирует
    /// уникальный индекс <c>ux_barber_services_barber_profile_id_hair_service_id</c>.
    /// Фильтр <c>is_active</c> сохранён: неактивная услуга слотов не даёт.
    /// </para>
    /// </remarks>
    /// <param name="context">Контекст базы данных.</param>
    /// <param name="barberProfileId">Профиль, чью услугу читаем.</param>
    /// <param name="hairServiceId">Услуга справочника (<c>hair_services.id</c>).</param>
    /// <returns>Запрос одной пары либо <c>null</c>, если услуга не найдена.</returns>
    internal static IQueryable<BarberSlotServiceLookup> BuildSlotServiceQuery(
        ApplicationDbContext context,
        Guid barberProfileId,
        Guid hairServiceId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.BarberServices
            .AsNoTracking()
            .Where(service => service.BarberProfileId == barberProfileId
                              && service.HairServiceId == hairServiceId
                              && service.IsActive)
            .Select(service => new BarberSlotServiceLookup(
                service.Id,
                service.HairServiceId,
                service.DurationMinutes));
    }

    public async Task<Result<BarberProfileResponse, Failure>> GetCardByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var profile = await Context.BarberProfiles
            .AsNoTracking()
            .Include(x => x.Salons)
                .ThenInclude(salon => salon.WorkingSchedules)
            .Include(x => x.Services)
            .Include(x => x.BarberSpecializations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                x => x.Id == id && x.IsActive && x.IsVerified,
                cancellationToken);

        if (profile is null)
        {
            return NotFound(ApiErrorCodes.BarberProfile.NotFound, nameof(BarberProfile), id);
        }

        var salonIds = profile.Salons.Select(salon => salon.SalonId).Distinct().ToArray();
        var salonsById = await Context.Salons
            .AsNoTracking()
            .Where(salon => salonIds.Contains(salon.Id) && salon.IsActive)
            .ToDictionaryAsync(salon => salon.Id, cancellationToken);

        var hairServiceIds = profile.Services
            .Select(service => service.HairServiceId)
            .Distinct()
            .ToArray();
        var hairServicesById = await Context.HairServices
            .AsNoTracking()
            .Where(service => hairServiceIds.Contains(service.Id))
            .ToDictionaryAsync(service => service.Id, cancellationToken);

        var specializationIds = profile.BarberSpecializations
            .Select(link => link.SpecializationId)
            .Distinct()
            .ToArray();
        var specializationsById = await Context.Specializations
            .AsNoTracking()
            .Where(specialization => specializationIds.Contains(specialization.Id))
            .ToDictionaryAsync(specialization => specialization.Id, cancellationToken);

        var services = profile.Services
            .Where(service => service.IsActive && hairServicesById.ContainsKey(service.HairServiceId))
            .OrderBy(service => hairServicesById[service.HairServiceId].Name)
            .Select(service =>
            {
                var hairService = hairServicesById[service.HairServiceId];

                return new BarberProfileServiceResponse(
                    service.Id,
                    service.HairServiceId,
                    hairService.Name,
                    hairService.Category,
                    hairService.DefaultDurationMinutes,
                    service.Price,
                    service.DurationMinutes,
                    service.IsActive);
            })
            .ToList();

        var salons = profile.Salons
            .Where(link => salonsById.ContainsKey(link.SalonId))
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => salonsById[link.SalonId].Name)
            .Select(link =>
            {
                var salon = salonsById[link.SalonId];

                return new BarberProfileSalonResponse(
                    link.Id,
                    link.SalonId,
                    salon.Name,
                    salon.Address,
                    salon.City,
                    salon.Country,
                    salon.TimeZoneId,
                    link.IsPrimary,
                    [.. link.WorkingSchedules
                        .OrderBy(schedule => schedule.DayOfWeek)
                        .Select(schedule => new WorkingScheduleResponse(
                            schedule.Id,
                            schedule.DayOfWeek,
                            schedule.StartTime,
                            schedule.EndTime,
                            schedule.IsDayOff))]);
            })
            .ToList();

        var specializations = profile.BarberSpecializations
            .Where(link => specializationsById.ContainsKey(link.SpecializationId))
            .OrderBy(link => specializationsById[link.SpecializationId].Name)
            .Select(link =>
            {
                var specialization = specializationsById[link.SpecializationId];

                return new BarberProfileSpecializationResponse(
                    link.Id,
                    link.SpecializationId,
                    specialization.Name,
                    specialization.Description);
            })
            .ToList();

        return new BarberProfileResponse(
            profile.Id,
            profile.UserId,
            profile.DisplayName.Value,
            profile.Grade,
            profile.Bio,
            profile.YearsOfExperience.Value,
            [.. profile.Languages],
            [.. profile.CertificatesAndTraining],
            profile.Rating,
            profile.ReviewsCount,
            [.. profile.Achievements],
            [.. profile.PaymentMethods],
            [.. profile.CommunicationFormat],
            [.. profile.Goodies],
            profile.LatePolicy,
            profile.IsActive,
            profile.IsVerified,
            services,
            salons,
            specializations);
    }

    public async Task<Result<bool, Failure>> IsOwnedByAsync(
        Guid barberProfileId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var isOwned = await Context.BarberProfiles
            .AsNoTracking()
            .AnyAsync(x => x.Id == barberProfileId && x.UserId == userId, cancellationToken);

        return isOwned;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(barberProfile);

        await Context.BarberProfiles.AddAsync(barberProfile, cancellationToken);

        return await SaveChangesAsync(ApiErrorCodes.BarberProfile.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(
            barberProfile,
            ApiErrorCodes.BarberProfile.UpdatePrefix,
            cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(barberProfile);

        Context.BarberProfiles.Remove(barberProfile);

        return await SaveChangesAsync(ApiErrorCodes.BarberProfile.DeletePrefix, cancellationToken);
    }

    /// <summary>
    /// Читает профиль по подготовленному запросу и переводит отсутствие строки в <c>404</c>.
    /// </summary>
    /// <remarks>
    /// Один <c>Include</c> коллекции не требует <c>AsSplitQuery</c>: EF Core выполняет один
    /// SELECT с JOIN, а не семь запросов полного агрегата. Отслеживание включено — вызывающие
    /// меняют профиль и сохраняют его через <c>UpdateAsync</c>, а <c>xmin</c> приходит вместе
    /// с прочитанной строкой.
    /// </remarks>
    private static async Task<Result<BarberProfile, Failure>> GetProfileAsync(
        IQueryable<BarberProfile> query,
        Guid id,
        CancellationToken cancellationToken)
    {
        var profile = await query.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (profile is null)
        {
            return NotFound(ApiErrorCodes.BarberProfile.NotFound, nameof(BarberProfile), id);
        }

        return profile;
    }

    /// <summary>
    /// Плоская строка админской карточки барбера до сборки вложенного чек-листа.
    /// </summary>
    internal sealed record AdminBarberDetailRow(
        Guid Id,
        Guid UserId,
        string DisplayName,
        string Email,
        string? PhoneNumber,
        string? AvatarUrl,
        Grade Grade,
        string? Bio,
        int YearsOfExperience,
        decimal? Rating,
        int ReviewsCount,
        bool IsActive,
        bool IsVerified,
        bool IsEmailVerified,
        int ServicesCount,
        int SalonsCount,
        int ScheduleDaysCount,
        int PhotosCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
