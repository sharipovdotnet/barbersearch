namespace BarberSearch.Contracts.BarberProfiles.BarberSchedule;

/// <summary>
/// Расписание барбера для экрана управления: салоны, недельный шаблон, исключения и отпуска.
/// </summary>
/// <remarks>
/// Отдаётся и подтверждённому, и ещё не подтверждённому профилю: барбер заполняет график
/// до верификации, а администратор видит его в чек-листе готовности. Публичная карточка
/// <c>GET api/barbers/{id}</c> для этого не подходит — она скрывает неподтверждённые профили.
/// </remarks>
/// <param name="BarberProfileId">Профиль барбера, которому принадлежит расписание.</param>
/// <param name="Salons">Салоны барбера; у каждого свой недельный шаблон и свои исключения.</param>
/// <param name="TimeOffs">Отпуска профиля: действующие и будущие, по возрастанию начала.</param>
public record BarberScheduleResponse(
    Guid BarberProfileId,
    IReadOnlyList<BarberScheduleSalonResponse> Salons,
    IReadOnlyList<BarberScheduleTimeOffResponse> TimeOffs);
