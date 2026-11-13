using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;

/// <summary>
/// Команда добавления нерабочего периода (отпуска) барбера.
/// </summary>
/// <remarks>
/// Времена передаются в UTC; проверку порядка границ и пересечения с другими
/// отпусками выполняет домен.
/// </remarks>
/// <param name="BarberProfileId">Профиль барбера — корень агрегата.</param>
/// <param name="StartAt">Начало отпуска в UTC.</param>
/// <param name="EndAt">Окончание отпуска в UTC.</param>
/// <param name="Reason">Пометка барбера или <c>null</c>.</param>
/// <param name="Force">
/// Сохранить отпуск, даже если в интервал попадают активные визиты. Осознанное подтверждение:
/// проверка прав и существования при этом не отключается.
/// </param>
public record AddBarberProfileTimeOffCommand(
    Guid BarberProfileId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason,
    bool Force = false) : ICommand;
