using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Domain.BarberSalons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.FindBarberProfileSalon;

/// <summary>
/// Обработчик запроса <see cref="FindBarberProfileSalonQuery"/>.
/// </summary>
/// <remarks>
/// Агрегат только читается: <c>UpdateAsync</c> здесь не вызывается, потому что запрос
/// не меняет состояние профиля.
/// </remarks>
public class FindBarberProfileSalonHandler
    : IQueryHandler<BarberSalonResponse, FindBarberProfileSalonQuery>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<FindBarberProfileSalonHandler> _logger;

    public FindBarberProfileSalonHandler(
        IBarberProfileRepository repository,
        ILogger<FindBarberProfileSalonHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Проверяет владельца профиля и возвращает запрошенный салон вместе с расписанием.
    /// </summary>
    /// <remarks>
    /// Загружаются только <c>user_id</c> профиля и одна строка <c>barber_salons</c>
    /// с расписанием: остальные салоны и четыре другие коллекции не нужны.
    /// Домен в ответе не возвращается: <see cref="BarberSalonResponse"/> собирается здесь,
    /// поэтому навигация <c>BarberSalon.BarberProfile</c> до Web-слоя не доходит.
    /// </remarks>
    public async Task<Result<BarberSalonResponse, Failure>> Handle(
        FindBarberProfileSalonQuery query,
        CancellationToken cancellationToken)
    {
        var ownerResult = await _repository.GetOwnerIdAsync(
            query.BarberProfileId,
            cancellationToken);
        if (ownerResult.IsFailure)
        {
            return ownerResult.Error;
        }

        var allowed = _currentUser.EnsureCanManage(ownerResult.Value);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var salonResult = await _repository.GetSalonAsync(
            query.BarberProfileId,
            query.SalonId,
            cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been found in barber profile {BarberProfileId}.",
            query.SalonId,
            query.BarberProfileId);

        return ToResponse(salonResult.Value);
    }

    /// <summary>
    /// Перекладывает доменный салон в ответ API: навигация на профиль наружу не выходит.
    /// </summary>
    private static BarberSalonResponse ToResponse(BarberSalon salon)
        => new(
            salon.Id,
            salon.BarberProfileId,
            salon.SalonId,
            salon.IsPrimary,
            [.. salon.WorkingSchedules.Select(schedule => new WorkingScheduleResponse(
                schedule.Id,
                schedule.DayOfWeek,
                schedule.StartTime,
                schedule.EndTime,
                schedule.IsDayOff))]);
}
