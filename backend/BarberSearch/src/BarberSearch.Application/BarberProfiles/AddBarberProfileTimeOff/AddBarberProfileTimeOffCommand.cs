using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;

/// <summary>
/// Команда добавления нерабочего периода (отпуска) барбера.
/// </summary>
/// <remarks>
/// Времена передаются в UTC; проверку порядка границ и пересечения с другими
/// отпусками выполняет домен.
/// </remarks>
public record AddBarberProfileTimeOffCommand(
    Guid BarberProfileId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason) : ICommand;
