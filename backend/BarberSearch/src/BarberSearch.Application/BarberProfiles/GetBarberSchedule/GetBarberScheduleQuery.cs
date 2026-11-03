using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;

namespace BarberSearch.Application.BarberProfiles.GetBarberSchedule;

/// <summary>
/// Запрос расписания барбера для экрана управления.
/// </summary>
/// <param name="BarberProfileId">Профиль барбера.</param>
/// <param name="From">Начало окна исключений; <c>null</c> — сегодня.</param>
/// <param name="To">Конец окна исключений; <c>null</c> — <paramref name="From"/> + 90 дней.</param>
public record GetBarberScheduleQuery(
    Guid BarberProfileId,
    DateOnly? From,
    DateOnly? To) : IQuery<BarberScheduleResponse>;
