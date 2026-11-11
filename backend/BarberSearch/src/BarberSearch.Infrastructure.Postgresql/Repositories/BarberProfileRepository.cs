using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.Admin;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.GetSalonBarbers;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Salons;
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
    /// <summary>
    /// Сколько значений отдаёт подсказка фильтра «дополнительные услуги».
    /// </summary>
    private const int ADDITIONAL_SERVICE_SUGGESTION_LIMIT = 100;

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

    public Task<Result<BarberProfile, Failure>> GetWithScheduleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetProfileAsync(
            Context.BarberProfiles
                .Include(x => x.Salons)
                    .ThenInclude(salon => salon.WorkingSchedules)
                .Include(x => x.Salons)
                    .ThenInclude(salon => salon.ScheduleExceptions)
                .Include(x => x.TimeOffs)
                .AsSplitQuery(),
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

    /// <summary>
    /// Фотография портфолио по паре (профиль, URL) — данные для раздачи файла.
    /// </summary>
    /// <remarks>
    /// Один join фотографии с профилем, без загрузки агрегата и других коллекций.
    /// Фильтра по флагам здесь нет намеренно: видимость решает вызывающий код, а владельцу
    /// и администратору скрытый кадр тоже нужен (иначе превью в кабинете были бы битыми).
    /// Индекс не добавляется: строк на профиль максимум десять, и запрос идёт по
    /// <c>ix_portfolio_photos_barber_profile_id_sort_order</c>.
    /// </remarks>
    public async Task<PortfolioPhotoAccess?> GetPortfolioPhotoAccessAsync(
        Guid barberProfileId,
        string url,
        CancellationToken cancellationToken = default)
    {
        return await (from photo in Context.PortfolioPhotos.AsNoTracking()
                      join profile in Context.BarberProfiles.AsNoTracking()
                          on photo.BarberProfileId equals profile.Id
                      where photo.BarberProfileId == barberProfileId
                            && photo.Url == url
                      select new PortfolioPhotoAccess(
                          profile.UserId,
                          photo.IsPublished,
                          photo.IsBlocked))
            .FirstOrDefaultAsync(cancellationToken);
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

    public async Task<Result<IReadOnlyList<BarberSalonSummaryResponse>, Failure>> GetSalonSummariesAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        // Привязки не фильтруются по is_active и модерации: «Мои салоны» — список работы
        // барбера, а не витрина. Владелец должен видеть салон на проверке или отклонённый
        // вместе с причиной отказа — иначе он не поймёт, почему салона нет в поиске.
        var rows = await (
                from link in Context.BarberSalons.AsNoTracking()
                join salon in Context.Salons.AsNoTracking() on link.SalonId equals salon.Id
                where link.BarberProfileId == barberProfileId
                orderby link.IsPrimary descending, salon.Name, salon.Id
                select new BarberSalonSummaryResponse(
                    salon.Id,
                    salon.Name,
                    salon.City,
                    salon.Address,
                    salon.TimeZoneId,
                    link.IsPrimary,
                    salon.Country,
                    salon.PostalCode,
                    salon.Latitude,
                    salon.Longitude,
                    salon.PhoneNumber,
                    salon.WebsiteUrl,
                    salon.OwnerUserId,
                    salon.ModerationStatus,
                    salon.RejectionReason,
                    salon.Kind,
                    salon.IsListed))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<IReadOnlyList<SalonBarberResponse>, Failure>> GetSalonBarbersAsync(
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        // Привязки не фильтруются по активности профиля или модерации салона:
        // владелец управляет составом и до публикации, а выключенный мастер должен
        // быть виден в списке, иначе его нельзя убрать.
        var rows = await (
                from link in Context.BarberSalons.AsNoTracking()
                join profile in Context.BarberProfiles.AsNoTracking()
                    on link.BarberProfileId equals profile.Id
                join user in Context.Users.AsNoTracking()
                    on profile.UserId equals user.Id
                where link.SalonId == salonId
                orderby profile.DisplayName.Value, profile.Id
                select new SalonBarberResponse(
                    profile.Id,
                    profile.UserId,
                    profile.DisplayName.Value,
                    user.Email,
                    profile.IsActive,
                    profile.IsVerified))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<IReadOnlyList<SalonBarberResponse>, Failure>>
        SearchSalonBarberCandidatesAsync(
            Guid salonId,
            string search,
            int limit,
            CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Result.Success<IReadOnlyList<SalonBarberResponse>, Failure>([]);
        }

        var pattern = BuildSearchPattern(search);

        // NOT EXISTS по связи, а не LEFT JOIN: привязка не размножает строку профиля,
        // а unique-индекс (barber_profile_id, salon_id) делает проверку дешёвой.
        var rows = await (
                from profile in Context.BarberProfiles.AsNoTracking()
                join user in Context.Users.AsNoTracking()
                    on profile.UserId equals user.Id
                where profile.IsActive
                      && !Context.BarberSalons.Any(link =>
                          link.SalonId == salonId && link.BarberProfileId == profile.Id)
                      && (EF.Functions.ILike(profile.DisplayName.Value, pattern, "\\")
                          || EF.Functions.ILike(user.Email, pattern, "\\"))
                orderby profile.DisplayName.Value, profile.Id
                select new SalonBarberResponse(
                    profile.Id,
                    profile.UserId,
                    profile.DisplayName.Value,
                    user.Email,
                    profile.IsActive,
                    profile.IsVerified))
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows;
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

        // Только публичная выдача, фильтры и курсор — в ApplySearchFilters: тест проверяет
        // их трансляцию через ToQueryString без базы (см. AdditionalServiceSearchQueryTests).
        var query = ApplySearchFilters(Context.BarberProfiles.AsNoTracking(), filter);

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
            .Select(profile => new BarberSearchRow(
                profile.Id,
                profile.UserId,
                profile.DisplayName.Value,
                profile.Grade,
                profile.YearsOfExperience.Value,
                profile.Languages.ToList(),
                profile.Rating,
                profile.ReviewsCount,
                profile.IsVerified,
                null,
                null,
                null,
                null,
                profile.AdditionalServices.ToList(),
                null))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new List<BarberSearchItemResponse>();
        }

        var profileIds = rows.Select(row => row.Id).ToArray();
        var userIds = rows.Select(row => row.UserId).ToArray();

        // Название и город основного салона — второй запрос на уже урезанный набор id.
        // Так основной запрос остаётся простым EXISTS-запросом без JOIN коллекций.
        var salons = await (
            from link in Context.BarberSalons.AsNoTracking()
            join salon in Context.Salons.AsNoTracking() on link.SalonId equals salon.Id
            where profileIds.Contains(link.BarberProfileId)
                  && salon.IsActive
                  && salon.ModerationStatus == SalonModerationStatus.Active
            orderby link.IsPrimary descending, link.Id
            select new
            {
                link.BarberProfileId,
                link.SalonId,
                salon.Name,
                salon.City,
                salon.Kind
            }).ToListAsync(cancellationToken);

        var primarySalons = salons
            .GroupBy(salon => salon.BarberProfileId)
            .ToDictionary(group => group.Key, group => group.First());

        // Аватар — свойство владельца, а не профиля: тот же приём, что у салона, — второй
        // запрос по уже урезанному набору id. Основной keyset-запрос остаётся на индексе,
        // без join'а users; количество строк по-прежнему ровно Limit.
        var avatars = await BuildSearchAvatarsQuery(Context, userIds)
            .ToListAsync(cancellationToken);

        var avatarUrls = avatars.ToDictionary(user => user.UserId, user => user.AvatarUrl);

        return rows
            .Select(row =>
            {
                avatarUrls.TryGetValue(row.UserId, out var avatarUrl);

                // Аватар и поля салона применяются одним `with`: строка поиска обходится
                // один раз, а `ToList` в конце — единственный.
                var enriched = primarySalons.TryGetValue(row.Id, out var salon)
                    ? row with
                    {
                        AvatarUrl = avatarUrl,
                        PrimarySalonId = salon.SalonId,
                        PrimarySalonName = salon.Name,
                        City = salon.City,
                        PrimaryPlaceKind = salon.Kind
                    }
                    : row with { AvatarUrl = avatarUrl };

                return new BarberSearchItemResponse(
                    enriched.Id,
                    enriched.DisplayName,
                    enriched.Grade,
                    enriched.YearsOfExperience,
                    enriched.Languages,
                    enriched.Rating,
                    enriched.ReviewsCount,
                    enriched.IsVerified,
                    enriched.PrimarySalonId,
                    enriched.PrimarySalonName,
                    enriched.City,
                    enriched.PrimaryPlaceKind,
                    enriched.AdditionalServices,
                    enriched.AvatarUrl);
            })
            .ToList();
    }

    /// <summary>
    /// Плоская строка keyset-выдачи поиска до догрузки салона и аватара.
    /// </summary>
    /// <remarks>
    /// <see cref="UserId"/> нужен только для второго запроса за аватаром и в контракт не попадает:
    /// иначе идентификатор владельца утёк бы в JSON, где его нет ни у одной строки выдачи.
    /// </remarks>
    private sealed record BarberSearchRow(
        Guid Id,
        Guid UserId,
        string DisplayName,
        Grade Grade,
        int YearsOfExperience,
        IReadOnlyList<Language> Languages,
        decimal? Rating,
        int ReviewsCount,
        bool IsVerified,
        Guid? PrimarySalonId,
        string? PrimarySalonName,
        string? City,
        PlaceKind? PrimaryPlaceKind,
        IReadOnlyList<string> AdditionalServices,
        string? AvatarUrl);

    /// <summary>
    /// Строит второй запрос поиска: аватары владельцев страницы по уже урезанному набору id.
    /// </summary>
    /// <remarks>
    /// Тернарник маски попадает в SQL (<c>CASE WHEN is_avatar_blocked THEN NULL ELSE avatar_url</c>),
    /// а не вычисляется в памяти: заблокированный пользователь не выпадает из выдачи, но его
    /// <c>AvatarUrl</c> приходит <c>null</c>, и карточка показывает инициалы. Фильтра
    /// <c>WHERE NOT is_avatar_blocked</c> здесь нет сознательно — иначе сломались бы счётчики
    /// и keyset-выдача.
    /// </remarks>
    /// <param name="context">Контекст базы.</param>
    /// <param name="userIds">Идентификаторы владельцев страницы.</param>
    internal static IQueryable<BarberSearchAvatarRow> BuildSearchAvatarsQuery(
        ApplicationDbContext context,
        Guid[] userIds)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new BarberSearchAvatarRow(
                user.Id,
                user.IsAvatarBlocked ? null : user.AvatarUrl));
    }

    /// <summary>
    /// Плоская строка второго запроса поиска: владелец и уже замаскированный аватар.
    /// </summary>
    /// <remarks>
    /// <see cref="UserId"/> нужен только для словаря и в контракт не попадает.
    /// </remarks>
    internal sealed record BarberSearchAvatarRow(Guid UserId, string? AvatarUrl);

    /// <summary>
    /// Предикаты публичного поиска: видимость, фильтры и keyset-курсор.
    /// </summary>
    /// <remarks>
    /// Вынесено из <see cref="SearchAsync"/>, чтобы unit-тест мог проверить трансляцию
    /// через <c>ToQueryString()</c> без базы. Подстрока дополнительной услуги обязана стать
    /// <c>EXISTS (SELECT 1 FROM unnest(additional_services) … WHERE … ILIKE …)</c>, а не
    /// клиентской фильтрацией: иначе keyset-пагинация вернула бы неполную выдачу.
    /// </remarks>
    internal IQueryable<BarberProfile> ApplySearchFilters(
        IQueryable<BarberProfile> query,
        BarberSearchFilter filter)
    {
        // Только публичная выдача: активный и подтверждённый профиль.
        query = query.Where(profile => profile.IsActive && profile.IsVerified);

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
                    && salon.ModerationStatus == SalonModerationStatus.Active
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

        if (filter.Language is { } language)
        {
            // Contains по primitive collection переводится в `languages @> ARRAY[1]`,
            // его покрывает GIN-индекс ix_barber_profiles_languages: коллекция
            // остаётся в базе, в память профили не тянутся.
            query = query.Where(profile => profile.Languages.Contains(language));
        }

        if (!string.IsNullOrWhiteSpace(filter.AdditionalService))
        {
            // Дополнительные услуги — свободный список, поэтому ищем подстроку, а не
            // точное значение: «бород» находит «Оформление бороды». EF Core 10 переводит
            // `Any` по primitive collection в `EXISTS (SELECT 1 FROM unnest(...))`, так
            // что профиль не материализуется. Спецсимволы `%`, `_`, `\` экранируются:
            // иначе «100%» находило бы всё.
            var additionalServicePattern = BuildSearchPattern(filter.AdditionalService);

            query = query.Where(profile => profile.AdditionalServices.Any(service =>
                EF.Functions.ILike(service, additionalServicePattern, "\\")));
        }

        if (filter.Date is { } date)
        {
            var dayOfWeek = date.DayOfWeek;

            // Дата — это «есть рабочий день в этот день недели хотя бы в одном активном салоне».
            // Конкретные свободные интервалы с учётом таймзоны и отпусков считает /slots.
            query = query.Where(profile => Context.BarberSalons.Any(link =>
                link.BarberProfileId == profile.Id
                && Context.Salons.Any(salon => salon.Id == link.SalonId
                    && salon.IsActive
                    && salon.ModerationStatus == SalonModerationStatus.Active)
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

        return query;
    }

    /// <summary>
    /// Подсказки фильтра «дополнительные услуги»: различные значения из видимых профилей.
    /// </summary>
    /// <remarks>
    /// Список собирается тем же предикатом видимости, что и поиск: предлагать значение,
    /// которого нет ни в одной выдаче, бессмысленно. <c>SelectMany</c> по primitive collection
    /// переводится в <c>JOIN LATERAL unnest(additional_services)</c>, <c>DISTINCT</c> схлопывает
    /// одинаковые строки разных барберов. Лимит ограничивает выпадающий список: подсказка —
    /// не справочник, и подсказка длиннее 100 строк всё равно не читается.
    /// </remarks>
    public async Task<Result<IReadOnlyList<string>, Failure>> GetAdditionalServicesAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await BuildAdditionalServicesQuery(Context.BarberProfiles.AsNoTracking())
            .ToListAsync(cancellationToken);

        return values;
    }

    /// <summary>
    /// Запрос подсказок фильтра «дополнительные услуги».
    /// </summary>
    /// <remarks>
    /// Предикат видимости тот же, что у поиска. <c>SelectMany</c> по primitive collection
    /// переводится в <c>JOIN LATERAL unnest(additional_services)</c>, <c>DISTINCT</c>
    /// схлопывает одинаковые строки разных барберов. Вынесено статическим, чтобы тест
    /// проверил трансляцию через <c>ToQueryString()</c> без базы.
    /// </remarks>
    internal static IQueryable<string> BuildAdditionalServicesQuery(
        IQueryable<BarberProfile> profiles)
        => profiles
            .Where(profile => profile.IsActive && profile.IsVerified)
            .SelectMany(profile => profile.AdditionalServices)
            .Distinct()
            .OrderBy(service => service)
            .Take(ADDITIONAL_SERVICE_SUGGESTION_LIMIT);

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

    /// <summary>
    /// Сколько профилей ждёт подтверждения — ровно <see cref="AdminBarberStatus.Pending"/>.
    /// </summary>
    /// <remarks>
    /// Предикат <c>is_verified = false</c> и <b>без</b> <c>is_active</c>: очередь «На проверке»
    /// в <c>BuildAdminBarbersQuery</c> фильтрует так же, а <c>is_active</c> спрятал бы
    /// неподтверждённый профиль, выключенный из поиска, и число разошлось бы с разделом.
    /// </remarks>
    public async Task<Result<int, Failure>> CountPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var count = await Context.BarberProfiles
            .AsNoTracking()
            .CountAsync(profile => !profile.IsVerified, cancellationToken);

        return Result.Success<int, Failure>(count);
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

        // Второй запрос вместо Include в проекции: карточка уже считает счётчики
        // подзапросами, а фотографии нужны отсортированными и одним плоским списком.
        var photos = await LoadAdminPhotosAsync(id, cancellationToken);

        return ToAdminDetailResponse(row, photos);
    }

    /// <summary>
    /// Все фотографии профиля для админской карточки: скрытые и заблокированные тоже.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания, порядок — <c>SortOrder</c>, затем <c>CreatedAt</c>:
    /// администратор должен видеть тот же порядок, что и владелец в кабинете.
    /// </remarks>
    private async Task<IReadOnlyList<BarberProfilePhotoResponse>> LoadAdminPhotosAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken)
    {
        return await Context.PortfolioPhotos
            .AsNoTracking()
            .Where(photo => photo.BarberProfileId == barberProfileId)
            .OrderBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .Select(photo => new BarberProfilePhotoResponse(
                photo.Id,
                photo.Url,
                photo.ThumbnailUrl,
                photo.Description,
                photo.SortOrder,
                photo.IsPublished,
                photo.IsBlocked))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Админская карточка барбера: профиль, владелец и счётчики готовности.
    /// </summary>
    /// <remarks>
    /// Коллекции агрегата не загружаются: услуги, салоны, фото и рабочие дни считаются
    /// подзапросами <c>COUNT</c>, поэтому карточка остаётся одним SQL-запросом.
    /// Рабочие дни схлопываются в дни недели: чек-листу нужен ответ «сколько разных дней
    /// расписано», а не число строк расписания по всем салонам. Счётчик фотографий
    /// считает только видимые (<c>IsPublished &amp;&amp; !IsBlocked</c>): чек-лист отражает
    /// публичную карточку, а не строки в БД.
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
                       photo => photo.BarberProfileId == profile.Id
                                && photo.IsPublished
                                && !photo.IsBlocked),
                   profile.CreatedAt,
                   profile.UpdatedAt,
                   user.IsAvatarBlocked);
    }

    private static AdminBarberDetailResponse ToAdminDetailResponse(
        AdminBarberDetailRow row,
        IReadOnlyList<BarberProfilePhotoResponse> photos)
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
            row.UpdatedAt,
            photos,
            row.IsAvatarBlocked);

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
                user.Email,
                user.AvatarUrl,
                user.IsAvatarBlocked
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
            row.profile.UpdatedAt,
            row.user.AvatarUrl,
            row.user.IsAvatarBlocked));
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

    public Task<Result<BarberSlotContext, Failure>> GetSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? hairServiceId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => GetSlotContextCoreAsync(
            barberProfileId,
            salonId,
            hairServiceId,
            date,
            requireVisibleProfile: true,
            cancellationToken);

    public Task<Result<BarberSlotContext, Failure>> GetOwnSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? hairServiceId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => GetSlotContextCoreAsync(
            barberProfileId,
            salonId,
            hairServiceId,
            date,
            requireVisibleProfile: false,
            cancellationToken);

    /// <summary>
    /// Общий расчёт контекста слотов; <paramref name="requireVisibleProfile"/> отделяет
    /// публичное чтение от кабинета барбера.
    /// </summary>
    private async Task<Result<BarberSlotContext, Failure>> GetSlotContextCoreAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? hairServiceId,
        DateOnly date,
        bool requireVisibleProfile,
        CancellationToken cancellationToken)
    {
        if (requireVisibleProfile)
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
        }

        // Салон выбирается среди активных: неактивный салон не должен давать слоты.
        var salonLinks = await (
            from link in Context.BarberSalons.AsNoTracking()
            join salon in Context.Salons.AsNoTracking() on link.SalonId equals salon.Id
            where link.BarberProfileId == barberProfileId
                  && salon.IsActive
                  && salon.ModerationStatus == SalonModerationStatus.Active
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

        // Исключение на дату важнее недельного шаблона: выходной отменяет обычный день,
        // а свои часы заменяют его границы.
        var dateOverride = await Context.ScheduleOverrides
            .AsNoTracking()
            .Where(entry => entry.BarberSalonId == salonLink.Id && entry.Date == date)
            .Select(entry => new
            {
                entry.IsDayOff,
                entry.StartTime,
                entry.EndTime
            })
            .FirstOrDefaultAsync(cancellationToken);

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

        var isWorkingDay = dateOverride is not null
            ? !dateOverride.IsDayOff
            : schedule is not null;
        var startTime = dateOverride?.StartTime ?? schedule?.StartTime ?? default;
        var endTime = dateOverride?.EndTime ?? schedule?.EndTime ?? default;

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
            isWorkingDay,
            startTime,
            endTime,
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
        var profile = await LoadCardProfileAsync(
            id,
            requireVisible: true,
            cancellationToken);

        if (profile is null)
        {
            return NotFound(ApiErrorCodes.BarberProfile.NotFound, nameof(BarberProfile), id);
        }

        var card = await BuildCardAsync(
            profile,
            includeInactiveServices: false,
            includeHiddenPhotos: false,
            cancellationToken);

        return card;
    }

    /// <summary>
    /// Собственная карточка барбера: без фильтра публичной видимости и со скрытыми услугами.
    /// </summary>
    /// <remarks>
    /// Проекция та же, что у публичной карточки (<see cref="BuildCardAsync"/>): контракт
    /// <c>BarberProfileResponse</c> не меняется, а фронтенд переиспользует тип и guard.
    /// Отличие только в предикатах: профиль читается любого статуса (барбер заполняет карточку
    /// до подтверждения), а <c>LoadServicesAsync</c> не режет выключенные строки.
    /// </remarks>
    public async Task<Result<BarberProfileResponse, Failure>> GetOwnCardByIdAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await LoadCardProfileAsync(
            barberProfileId,
            requireVisible: false,
            cancellationToken);

        if (profile is null)
        {
            return NotFound(
                ApiErrorCodes.BarberProfile.NotFound,
                nameof(BarberProfile),
                barberProfileId);
        }

        var card = await BuildCardAsync(
            profile,
            includeInactiveServices: true,
            includeHiddenPhotos: true,
            cancellationToken);

        return card;
    }

    /// <summary>
    /// Читает профиль с коллекциями карточки: салоны с расписанием, услуги, специализации
    /// и портфолио.
    /// </summary>
    /// <remarks>
    /// <paramref name="requireVisible"/> — единственное отличие публичного чтения от
    /// собственного: <c>IsActive &amp;&amp; IsVerified</c> против «любой статус».
    /// <c>AsSplitQuery</c> обязателен: четыре коллекции в одном запросе дали бы декартово
    /// произведение. Чтение идёт без отслеживания — карточка только читается.
    /// </remarks>
    private async Task<BarberProfile?> LoadCardProfileAsync(
        Guid id,
        bool requireVisible,
        CancellationToken cancellationToken)
    {
        var query = Context.BarberProfiles
            .AsNoTracking()
            .Include(x => x.Salons)
                .ThenInclude(salon => salon.WorkingSchedules)
            .Include(x => x.Services)
            .Include(x => x.BarberSpecializations)
            .Include(x => x.PortfolioPhotos)
            .AsSplitQuery();

        if (requireVisible)
        {
            query = query.Where(x => x.Id == id && x.IsActive && x.IsVerified);
        }
        else
        {
            query = query.Where(x => x.Id == id);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Проекция загруженного агрегата в <see cref="BarberProfileResponse"/>.
    /// </summary>
    /// <remarks>
    /// Одна проекция на оба чтения: публичное и собственное. Дублировать восемьдесят строк
    /// ради одного флага нельзя — карточка обязана меняться в одном месте. Флаг
    /// <paramref name="includeInactiveServices"/> уходит в <c>LoadServicesAsync</c>,
    /// <paramref name="includeHiddenPhotos"/> — в фильтр портфолио: публичная карточка
    /// отдаёт только <c>IsPublished &amp;&amp; !IsBlocked</c>, собственная — все фотографии
    /// с обоими флагами, чтобы барбер видел «Скрыто» и «Скрыто администратором».
    /// </remarks>
    private async Task<BarberProfileResponse> BuildCardAsync(
        BarberProfile profile,
        bool includeInactiveServices,
        bool includeHiddenPhotos,
        CancellationToken cancellationToken)
    {
        var services = await LoadServicesAsync(
            profile,
            includeInactiveServices,
            cancellationToken);
        var salons = await LoadSalonsAsync(profile, cancellationToken);

        var photos = profile.PortfolioPhotos
            .Where(photo => includeHiddenPhotos
                            || (photo.IsPublished && !photo.IsBlocked))
            .OrderBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .Select(photo => new BarberProfilePhotoResponse(
                photo.Id,
                photo.Url,
                photo.ThumbnailUrl,
                photo.Description,
                photo.SortOrder,
                photo.IsPublished,
                photo.IsBlocked))
            .ToList();

        var specializationIds = profile.BarberSpecializations
            .Select(link => link.SpecializationId)
            .Distinct()
            .ToArray();
        var specializationsById = await Context.Specializations
            .AsNoTracking()
            .Where(specialization => specializationIds.Contains(specialization.Id))
            .ToDictionaryAsync(specialization => specialization.Id, cancellationToken);

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

        // Аватар — это поле другого агрегата (User), поэтому оно читается отдельным узким
        // запросом по первичному ключу. Цена — один индексный lookup `users_pkey` на чтение
        // карточки; отдельного join'а в тяжёлый запрос карточки (четыре Include под
        // AsSplitQuery) нет, и новых индексов не появляется. Пользователь без аватара даёт
        // null, отсутствующая строка (историческая ссылка) — тоже null: карточка не падает.
        // Скрытый администратором кадр маскируется в SQL (CASE), а не в памяти: наружу уходит
        // null, поэтому карточка показывает инициалы.
        var avatarUrl = await BuildCardAvatarQuery(Context, profile.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        return new BarberProfileResponse(
            profile.Id,
            profile.UserId,
            avatarUrl,
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
            specializations,
            [.. profile.AdditionalServices],
            photos);
    }

    /// <summary>
    /// Строит узкое чтение аватара владельца карточки: маска по флагу блокировки в SQL.
    /// </summary>
    /// <remarks>
    /// Один <c>SELECT CASE WHEN is_avatar_blocked THEN NULL ELSE avatar_url END</c> по
    /// первичному ключу <c>users_pkey</c>, без join'а. Тернарник обязан остаться в проекции:
    /// если материализовать пару значений и маскировать в памяти, EF перестанет видеть
    /// <c>CASE</c> и транслировать маску в SQL. Вынесено в <c>internal static</c>, чтобы
    /// unit-тест проверил трансляцию через <c>ToQueryString()</c>.
    /// </remarks>
    /// <param name="context">Контекст базы.</param>
    /// <param name="userId">Владелец профиля.</param>
    internal static IQueryable<string?> BuildCardAvatarQuery(
        ApplicationDbContext context,
        Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.IsAvatarBlocked ? null : user.AvatarUrl);
    }

    /// <summary>
    /// Услуги и салоны собственного профиля для формы ручной записи.
    /// </summary>
    /// <remarks>
    /// В отличие от публичной карточки, профиль не обязан быть активным и подтверждённым:
    /// кабинет доступен и до публикации. Активность самих услуг и салонов сохраняется —
    /// записать клиента на скрытую услугу нельзя. Недельное расписание салона нужно форме,
    /// чтобы показать выходной день; слоты считает отдельный эндпоинт.
    /// </remarks>
    public async Task<Result<BarberBookingOptionsResponse, Failure>> GetBookingOptionsAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await Context.BarberProfiles
            .AsNoTracking()
            .Include(x => x.Salons)
                .ThenInclude(salon => salon.WorkingSchedules)
            .Include(x => x.Services)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == barberProfileId, cancellationToken);

        if (profile is null)
        {
            return NotFound(
                ApiErrorCodes.BarberProfile.NotFound,
                nameof(BarberProfile),
                barberProfileId);
        }

        var services = await LoadServicesAsync(
            profile,
            includeInactiveServices: false,
            cancellationToken);
        var salons = await LoadSalonsAsync(profile, cancellationToken);

        return new BarberBookingOptionsResponse(services, salons);
    }

    /// <summary>
    /// Услуги профиля с названием и категорией из справочника.
    /// </summary>
    /// <remarks>
    /// Читает только <c>hair_services</c>: услуги и их цены уже загружены в агрегат.
    /// Услуга, у которой справочник удалён, не показывается — как и в публичной карточке.
    /// <paramref name="includeInactiveServices"/> = <c>false</c> — фильтр публичной карточки
    /// и формы записи: скрытая услуга не показывается клиенту. <c>true</c> — собственный
    /// прайс барбера: скрытая строка остаётся в списке со статусом «Скрыта», иначе её нельзя
    /// было бы вернуть в работу.
    /// </remarks>
    private async Task<List<BarberProfileServiceResponse>> LoadServicesAsync(
        BarberProfile profile,
        bool includeInactiveServices,
        CancellationToken cancellationToken)
    {
        var hairServiceIds = profile.Services
            .Select(service => service.HairServiceId)
            .Distinct()
            .ToArray();
        var hairServicesById = await Context.HairServices
            .AsNoTracking()
            .Where(service => hairServiceIds.Contains(service.Id))
            .ToDictionaryAsync(service => service.Id, cancellationToken);

        return profile.Services
            .Where(service => (includeInactiveServices || service.IsActive)
                              && hairServicesById.ContainsKey(service.HairServiceId))
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
    }

    /// <summary>
    /// Активные салоны профиля с недельным расписанием пары (барбер, салон).
    /// </summary>
    /// <remarks>
    /// Порядок — основной салон, затем по названию: форма открывается на основном салоне.
    /// Неактивный салон скрыт, даже если связь с барбером осталась.
    /// </remarks>
    private async Task<List<BarberProfileSalonResponse>> LoadSalonsAsync(
        BarberProfile profile,
        CancellationToken cancellationToken)
    {
        var salonIds = profile.Salons.Select(salon => salon.SalonId).Distinct().ToArray();
        var salonsById = await Context.Salons
            .AsNoTracking()
            .Where(salon => salonIds.Contains(salon.Id)
                            && salon.IsActive
                            && salon.ModerationStatus == SalonModerationStatus.Active)
            .ToDictionaryAsync(salon => salon.Id, cancellationToken);

        return profile.Salons
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
                    salon.Kind,
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
        DateTimeOffset UpdatedAt,
        bool IsAvatarBlocked);
}
