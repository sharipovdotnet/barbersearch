using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="BarberProfile"/>.
/// </summary>
public sealed class BarberProfileRepository : RepositoryBase<BarberProfile>, IBarberProfileRepository
{
    public BarberProfileRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<Result<BarberProfile, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var profile = await FullAggregate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (profile is null)
        {
            return NotFound("barberProfile.getById.notFound", nameof(BarberProfile), id);
        }

        return profile;
    }

    public async Task<Result<BarberProfile, Failure>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await FullAggregate()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return new Failure(Error.NotFound(
                "barberProfile.getByUserId.notFound",
                $"Barber profile of user '{userId}' was not found.",
                userId));
        }

        return profile;
    }

    public async Task<Result<BarberProfile, Failure>> GetForRatingUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Узкое чтение корня: пять коллекций для UpdateRating не нужны, а row lock
        // сериализует конкурентные пересчёты одного профиля (AGENT.md §12.2).
        // Сырой SQL намеренно не композируется с LINQ: FOR UPDATE должен остаться
        // в том же запросе, что и чтение строки.
        var profiles = await Context.BarberProfiles
            .FromSqlInterpolated($"SELECT * FROM barber_profiles WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);

        var profile = profiles.SingleOrDefault();

        if (profile is null)
        {
            return NotFound("barberProfile.getById.notFound", nameof(BarberProfile), id);
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

    public async Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(page, pageSize, "barberProfile.getPaged") is { } pagingError)
        {
            return pagingError;
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
                "barberProfile.getService.notFound",
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

    public async Task<Result<IReadOnlyList<BarberSearchItemResponse>, Failure>> SearchAsync(
        BarberSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Limit <= 0)
        {
            return new Failure(Error.Validation(
                "barberSearch.search.limit",
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

        if (filter.ServiceId is { } serviceId)
        {
            query = query.Where(profile => Context.BarberServices.Any(service =>
                service.BarberProfileId == profile.Id
                && service.HairServiceId == serviceId
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

    public async Task<Result<BarberSlotContext, Failure>> GetSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? serviceId,
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
                "barberProfile.getSlots.notFound",
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
                "barberSlots.salon.notFound",
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
                "barberSlots.salon.notFound",
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

        var durationMinutes = 30;

        if (serviceId is { } requestedServiceId)
        {
            var serviceDuration = await Context.BarberServices
                .AsNoTracking()
                .Where(service => service.BarberProfileId == barberProfileId
                                  && service.HairServiceId == requestedServiceId
                                  && service.IsActive)
                .Select(service => (int?)service.DurationMinutes)
                .FirstOrDefaultAsync(cancellationToken);

            if (serviceDuration is null)
            {
                return new Failure(Error.NotFound(
                    "barberSlots.service.notFound",
                    $"The barber profile '{barberProfileId}' does not provide hair service '{requestedServiceId}'.",
                    requestedServiceId));
            }

            durationMinutes = serviceDuration.Value;
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
            timeOffs);
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
            return NotFound("barberProfile.getCardById.notFound", nameof(BarberProfile), id);
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

        return await SaveChangesAsync("barberProfile.add", cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(barberProfile, "barberProfile.update", cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(barberProfile);

        Context.BarberProfiles.Remove(barberProfile);

        return await SaveChangesAsync("barberProfile.delete", cancellationToken);
    }

    /// <summary>
    /// Запрос профиля со всеми дочерними коллекциями.
    /// </summary>
    /// <remarks>
    /// <c>AsSplitQuery</c> обязателен: пять коллекций в одном JOIN дают декартово произведение
    /// строк, то есть и лишний трафик, и неверный результат при агрегатах.
    /// </remarks>
    private IQueryable<BarberProfile> FullAggregate()
        => Context.BarberProfiles
            .Include(x => x.Salons)
                .ThenInclude(salon => salon.WorkingSchedules)
            .Include(x => x.Services)
            .Include(x => x.PortfolioPhotos)
            .Include(x => x.BarberSpecializations)
            .Include(x => x.TimeOffs)
            .AsSplitQuery();
}
