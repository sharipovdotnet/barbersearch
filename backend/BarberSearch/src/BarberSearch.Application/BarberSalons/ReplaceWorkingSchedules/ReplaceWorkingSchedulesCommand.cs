using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.WorkingSchedules;

namespace BarberSearch.Application.BarberSalons.ReplaceWorkingSchedules;

/// <summary>
/// Команда полной замены недельного шаблона пары (барбер, салон).
/// </summary>
/// <param name="BarberProfileId">Профиль барбера — корень агрегата.</param>
/// <param name="SalonId">Салон, чей шаблон меняется.</param>
/// <param name="Days">Ровно семь дней недели.</param>
/// <param name="Force">
/// Сохранить правку, даже если активные визиты окажутся вне новых часов. Осознанное
/// подтверждение: проверка прав и существования при этом не отключается.
/// </param>
public record ReplaceWorkingSchedulesCommand(
    Guid BarberProfileId,
    Guid SalonId,
    IReadOnlyList<WorkingDaySchedule> Days,
    bool Force = false) : ICommand;
